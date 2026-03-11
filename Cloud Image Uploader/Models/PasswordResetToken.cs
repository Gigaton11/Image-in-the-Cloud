using Amazon.DynamoDBv2.DataModel;

namespace Cloud_Image_Uploader.Models;

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