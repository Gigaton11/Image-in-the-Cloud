using Amazon.DynamoDBv2.DataModel;

namespace Cloud_Image_Uploader.Models;

[DynamoDBTable("UserAccounts")]
public class UserAccount
{
    [DynamoDBHashKey]
    public required string UserId { get; set; }

    public required string UserName { get; set; }

    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}