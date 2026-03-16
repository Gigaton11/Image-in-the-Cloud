using Amazon.DynamoDBv2.DataModel;

namespace Cloud_Image_Uploader.Models;

// Single-use, time-limited token record for the password-reset flow.
// The token URL has the form "{TokenId}.{secret}"; only the SHA-256 hash
// of secret is persisted so a database read cannot be used to replay the token.
[DynamoDBTable("PasswordResetTokens")]
public class PasswordResetToken
{
    [DynamoDBHashKey]
    public required string TokenId { get; set; }

    public required string UserId { get; set; }

    public required string SecretHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public bool Used { get; set; }
}