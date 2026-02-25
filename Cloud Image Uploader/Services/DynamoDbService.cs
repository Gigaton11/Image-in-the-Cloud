using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Cloud_Image_Uploader.Models;

public class DynamoDbService
{
    private readonly IDynamoDBContext _dbContext;

    public DynamoDbService(IAmazonDynamoDB dynamoDbClient)
    {
        _dbContext = new DynamoDBContext(dynamoDbClient);
    }

    public async Task TrackUploadAsync(FileMetadata fileMetadata)
    {
        await _dbContext.SaveAsync(fileMetadata);
    }
    public async Task TrackDownloadAsync(string fileId, string downloadedBy)
    {
        await _dbContext.SaveAsync(new DownloadRecord
        {
            FileId = fileId,
            DownloadTime = DateTime.UtcNow,
            DownloadedBy = downloadedBy
        });
    }
    public async Task<List<FileMetadata>> GetRecentUploads(int count = 10)
    {
        var conditions = new List<ScanCondition>();
        var results = await _dbContext.ScanAsync<FileMetadata>(conditions)
            .GetRemainingAsync();

        return results.OrderByDescending(x => x.UploadTime).Take(count).ToList();
    }
}


[DynamoDBTable("FileMetadata")]
public class FileMetadata
{
    [DynamoDBHashKey]
    public required string FileId { get; set; }
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public required string ContentType { get; set; }
    public DateTime UploadTime { get; set; }
    public string? UploadedBy { get; set; }
}

[DynamoDBTable("DownloadRecords")]
public class DownloadRecord
{
    [DynamoDBHashKey]
    public string FileId { get; set; }
    public DateTime DownloadTime { get; set; }
    public string DownloadedBy { get; set; }
}
