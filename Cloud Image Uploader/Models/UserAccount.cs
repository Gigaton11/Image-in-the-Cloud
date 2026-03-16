using Amazon.DynamoDBv2.DataModel;

namespace Cloud_Image_Uploader.Models;

// DynamoDB record for a registered user.
// UserId is the normalised (upper-cased) username and acts as the table hash key,
// enabling O(1) username lookups without a secondary index.
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