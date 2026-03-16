using Amazon.DynamoDBv2.DataModel;
using Cloud_Image_Uploader.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;

namespace Cloud_Image_Uploader.Services;

//
// Handles user registration, authentication, and password-reset token lifecycle.
// Accounts are stored in the DynamoDB UserAccounts table keyed by normalised username.
//
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

    // Creates a new user account after checking that the username and email are not already taken.
    // Returns the new UserAccount on success, or an error message on conflict.
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

    // Looks up the user by username or email and verifies the password hash.
    // Returns null if the user is not found or the password does not match.
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

    // Generates a signed password-reset token of the form "{tokenId}.{secret}" and persists
    // a record that stores only the SHA-256 hash of the secret to prevent database-read replay attacks.
    // Returns null values when the identifier does not match any account (without leaking that fact).
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

    // Validates the token format, verifies the secret hash, checks expiry and single-use,
    // then updates the account password and marks the token as used.
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

    // Username and email are stored case-insensitively to prevent duplicate accounts
    // that differ only in casing (e.g. "Alice" vs "alice").
    private static string NormalizeUserName(string userName)
    {
        return userName.Trim().ToUpperInvariant();
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    // Attempts a direct DynamoDB key lookup by username first, then falls back to a
    // full table scan filtered by email. The scan can be replaced with a GSI lookup
    // once an email index is added to the UserAccounts table.
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

    // Returns the uppercase hex-encoded SHA-256 digest of a UTF-8 string.
    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}