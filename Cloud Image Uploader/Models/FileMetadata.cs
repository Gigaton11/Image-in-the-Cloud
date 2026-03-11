using Amazon.DynamoDBv2.DataModel;

namespace Cloud_Image_Uploader.Models;

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

    public string? UploadedBy { get; set; }

    public string? OwnerUserId { get; set; }

    public bool? IsPublic { get; set; }
}
   
