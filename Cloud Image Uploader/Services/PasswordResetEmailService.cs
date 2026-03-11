using System.Text.Encodings.Web;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Cloud_Image_Uploader.Models;
using Microsoft.Extensions.Options;

namespace Cloud_Image_Uploader.Services;

public class PasswordResetEmailService
{
    private readonly IAmazonSimpleEmailServiceV2 _sesClient;
    private readonly EmailOptions _emailOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PasswordResetEmailService> _logger;

    public PasswordResetEmailService(
        IAmazonSimpleEmailServiceV2 sesClient,
        IOptions<EmailOptions> emailOptions,
        IWebHostEnvironment environment,
        ILogger<PasswordResetEmailService> logger)
    {
        _sesClient = sesClient;
        _emailOptions = emailOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<(bool Success, string? ErrorMessage, string? DevelopmentResetLink)> SendPasswordResetAsync(string recipientEmail, string resetUrl)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return (false, "Password reset email recipient is missing.", null);
        }

        if (string.IsNullOrWhiteSpace(resetUrl))
        {
            return (false, "Password reset link could not be generated.", null);
        }

        if (!_emailOptions.EnableSesDelivery)
        {
            return ResolveFallback(
                "SES delivery is disabled by configuration. Showing reset link fallback.",
                resetUrl,
                allowOutsideDevelopment: true);
        }

        if (!HasConfiguredFromAddress(_emailOptions.FromAddress))
        {
            return ResolveFallback("Email sender address is not configured. Set Email:FromAddress to your verified SES sender.", resetUrl);
        }

        // Encode before embedding in HTML so malformed URLs cannot break the message template.
        var encodedUrl = HtmlEncoder.Default.Encode(resetUrl);
        var subject = "Reset your Cloud Image Uploader password";
        var textBody = $"Use this link to reset your Cloud Image Uploader password: {resetUrl}\n\nThis link expires in 30 minutes.";
        var htmlBody = $"""
<!DOCTYPE html>
<html lang=\"en\">
<body style=\"font-family: Arial, sans-serif; line-height: 1.6; color: #1f2937;\">
    <p>Use the link below to reset your Cloud Image Uploader password.</p>
    <p><a href=\"{encodedUrl}\">Reset your password</a></p>
    <p>If the button does not open, copy this URL into your browser:</p>
    <p>{encodedUrl}</p>
    <p>This link expires in 30 minutes.</p>
</body>
</html>
""";

        var fromAddress = string.IsNullOrWhiteSpace(_emailOptions.FromName)
            ? _emailOptions.FromAddress
            : $"{_emailOptions.FromName} <{_emailOptions.FromAddress}>";

        try
        {
            var request = new SendEmailRequest
            {
                FromEmailAddress = fromAddress,
                Destination = new Destination
                {
                    ToAddresses = new List<string> { recipientEmail }
                },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content
                        {
                            Charset = "UTF-8",
                            Data = subject
                        },
                        Body = new Body
                        {
                            Text = new Content
                            {
                                Charset = "UTF-8",
                                Data = textBody
                            },
                            Html = new Content
                            {
                                Charset = "UTF-8",
                                Data = htmlBody
                            }
                        }
                    }
                }
            };

            await _sesClient.SendEmailAsync(request);
            _logger.LogInformation("Password reset email sent to {RecipientEmail}", recipientEmail);
            return (true, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {RecipientEmail}", recipientEmail);
            return ResolveFallback("Password reset email could not be delivered. Check SES identity verification and sender configuration.", resetUrl);
        }
    }

    private (bool Success, string? ErrorMessage, string? DevelopmentResetLink) ResolveFallback(
        string errorMessage,
        string resetUrl,
        bool allowOutsideDevelopment = false)
    {
        // Development fallback keeps the reset flow testable even when SES is disabled/misconfigured.
        var canShowResetLink = _emailOptions.ShowResetLinkInDevelopment && (allowOutsideDevelopment || _environment.IsDevelopment());
        if (canShowResetLink)
        {
            _logger.LogWarning("{ErrorMessage} Falling back to development reset link preview.", errorMessage);
            return (true, null, resetUrl);
        }

        return (false, errorMessage, null);
    }

    private static bool HasConfiguredFromAddress(string? fromAddress)
    {
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            return false;
        }

        var trimmed = fromAddress.Trim();
        return !trimmed.Contains("example.com", StringComparison.OrdinalIgnoreCase);
    }
}