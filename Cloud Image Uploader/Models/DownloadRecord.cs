using Amazon.DynamoDBv2.DataModel;

namespace Cloud_Image_Uploader.Models;

[DynamoDBTable("DownloadRecords")]
public class DownloadRecord
{
    [DynamoDBHashKey]
    public required string FileId { get; set; }

    public DateTime DownloadTime { get; set; }

    public required string DownloadedBy { get; set; }
}