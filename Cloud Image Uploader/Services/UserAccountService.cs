using Amazon.DynamoDBv2.DataModel;
using Cloud_Image_Uploader.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;

namespace Cloud_Image_Uploader.Services;

public class UserAccountService
{
    private static readonly PasswordHasher<UserAccount> PasswordHasher = new();
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);

    private readonly IDynamoDBContext _dbContext;
    private readonly ILogger<UserAccountService> _logger;

    public UserAccountService(IDynamoDBContext dbContext, ILogger<UserAccountService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<(bool Success, string? ErrorMessage, UserAccount? User)> RegisterAsync(string userName, string email, string password)
    {
        var trimmedUserName = userName.Trim();
        var trimmedEmail = email.Trim();
        var normalizedUserName = NormalizeUserName(trimmedUserName);
        var normalizedEmail = NormalizeEmail(trimmedEmail);

        var existingUser = await _dbContext.LoadAsync<UserAccount>(normalizedUserName);
        if (existingUser != null)
        {
            return (false, "That username is already taken.", null);
        }

        // Email is not indexed yet, so uniqueness is validated with a full scan.
        var allUsers = await _dbContext.ScanAsync<UserAccount>(new List<ScanCondition>()).GetRemainingAsync();
        if (allUsers.Any(x => string.Equals(NormalizeEmail(x.Email), normalizedEmail, StringComparison.Ordinal)))
        {
            return (false, "That email is already registered.", null);
        }

        var user = new UserAccount
        {
            UserId = normalizedUserName,
            UserName = trimmedUserName,
            Email = trimmedEmail,
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };

        user.PasswordHash = PasswordHasher.HashPassword(user, password);
        await _dbContext.SaveAsync(user);

        _logger.LogInformation("User account created: {UserId}", user.UserId);
        return (true, null, user);
    }

    public async Task<UserAccount?> AuthenticateAsync(string identifier, string password)
    {
        var user = await FindByIdentifierAsync(identifier);
        if (user == null)
        {
            _logger.LogWarning("Authentication failed for unknown identifier: {Identifier}", identifier);
            return null;
        }

        var result = PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Authentication failed for user: {UserId}", user.UserId);
            return null;
        }

        return user;
    }

    public async Task<(string? Token, string? RecipientEmail)> CreatePasswordResetTokenAsync(string identifier)
    {
        var user = await FindByIdentifierAsync(identifier);
        if (user == null)
        {
            _logger.LogWarning("Password reset requested for unknown identifier: {Identifier}", identifier);
            return (null, null);
        }

        var tokenId = Guid.NewGuid().ToString("N");
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(secretBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var nowUtc = DateTime.UtcNow;
        // Store only a hash of the secret so database reads cannot be used to replay reset tokens.
        var record = new PasswordResetToken
        {
            TokenId = tokenId,
            UserId = user.UserId,
            SecretHash = ComputeSha256(secret),
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(PasswordResetTokenLifetime),
            Used = false
        };

        await _dbContext.SaveAsync(record);
        _logger.LogInformation("Password reset token created for user: {UserId}", user.UserId);

        return ($"{tokenId}.{secret}", user.Email);
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.Contains('.'))
        {
            return (false, "Invalid reset token.");
        }

        var parts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return (false, "Invalid reset token.");
        }

        var tokenId = parts[0];
        var secret = parts[1];

        var tokenRecord = await _dbContext.LoadAsync<PasswordResetToken>(tokenId);
        if (tokenRecord == null)
        {
            return (false, "Reset token not found.");
        }

        if (tokenRecord.Used)
        {
            return (false, "Reset token was already used.");
        }

        if (DateTime.UtcNow > tokenRecord.ExpiresAtUtc)
        {
            return (false, "Reset token has expired.");
        }

        if (!string.Equals(tokenRecord.SecretHash, ComputeSha256(secret), StringComparison.Ordinal))
        {
            return (false, "Invalid reset token.");
        }

        var user = await _dbContext.LoadAsync<UserAccount>(tokenRecord.UserId);
        if (user == null)
        {
            return (false, "User account no longer exists.");
        }

        user.PasswordHash = PasswordHasher.HashPassword(user, newPassword);
        await _dbContext.SaveAsync(user);

        tokenRecord.Used = true;
        await _dbContext.SaveAsync(tokenRecord);

        _logger.LogInformation("Password reset completed for user: {UserId}", user.UserId);
        return (true, "Password has been reset successfully.");
    }

    private static string NormalizeUserName(string userName)
    {
        return userName.Trim().ToUpperInvariant();
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    private async Task<UserAccount?> FindByIdentifierAsync(string identifier)
    {
        var trimmed = identifier.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var byUserName = await _dbContext.LoadAsync<UserAccount>(NormalizeUserName(trimmed));
        if (byUserName != null)
        {
            return byUserName;
        }

        var normalizedEmail = NormalizeEmail(trimmed);
        // No email GSI yet, so email lookup falls back to a scan.
        var users = await _dbContext.ScanAsync<UserAccount>(new List<ScanCondition>()).GetRemainingAsync();
        return users.FirstOrDefault(x => string.Equals(NormalizeEmail(x.Email), normalizedEmail, StringComparison.Ordinal));
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}