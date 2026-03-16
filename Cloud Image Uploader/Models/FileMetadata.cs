using Amazon.DynamoDBv2.DataModel;

namespace Cloud_Image_Uploader.Models;

// DynamoDB record that tracks every uploaded file's ownership, expiry, and visibility.
[DynamoDBTable("FileMetadata")]
public class FileMetadata
{
    [DynamoDBHashKey]
    public required string FileId { get; set; }

    public required string FileName { get; set; }

    public long FileSize { get; set; }

    public required string ContentType { get; set; }

    public DateTime UploadTime { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    // The display name of the uploader ("anonymous" for guests, username for signed-in users).
    public string? UploadedBy { get; set; }

    // Null for guest uploads, set to UserAccount.UserId for authenticated uploads.
    // Determines ownership for delete and visibility-change operations.
    public string? OwnerUserId { get; set; }

    // Null on legacy records: DynamoDbService.IsFilePublic() resolves the effective value.
    public bool? IsPublic { get; set; }
}
   
