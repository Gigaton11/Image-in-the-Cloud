using Amazon.DynamoDBv2.DataModel;

namespace Cloud_Image_Uploader.Models;

// Audit log entry written on every successful file download.
// FileId is not globally unique across records — multiple downloads of the same
// file each produce their own DownloadRecord keyed by the same FileId value,
// so this table effectively stores append-only rows via SaveAsync overwrite semantics.
[DynamoDBTable("DownloadRecords")]
public class DownloadRecord
{
    [DynamoDBHashKey]
    public required string FileId { get; set; }

    public DateTime DownloadTime { get; set; }

    public required string DownloadedBy { get; set; }
}