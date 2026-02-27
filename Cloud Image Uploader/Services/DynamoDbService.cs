using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Cloud_Image_Uploader.Models;

//
// Service for managing file metadata and download tracking in AWS DynamoDB.
// Stores upload information and download records for analytics and security.
//
public class DynamoDbService
{
    // DynamoDB context for ORM-like operations.
    private readonly IDynamoDBContext _dbContext;

    // Logger for DynamoDbService operations.
    private readonly ILogger<DynamoDbService> _logger;

    // Constructor that initializes the DynamoDB context.
    public DynamoDbService(IAmazonDynamoDB dynamoDbClient, ILogger<DynamoDbService> logger)
    {
        _logger = logger;
        _dbContext = new DynamoDBContext(dynamoDbClient);
        _logger.LogInformation("DynamoDbService initialized");
    }

    //
    // Saves file metadata when a new file is uploaded.
    // Records the filename, size, upload time, and uploader information.
    //
    public async Task TrackUploadAsync(FileMetadata fileMetadata)
    {
        try
        {
            _logger.LogInformation("Tracking upload: FileId={FileId}, FileName={FileName}, Size={FileSizeBytes}", 
                fileMetadata.FileId, fileMetadata.FileName, fileMetadata.FileSize);
            await _dbContext.SaveAsync(fileMetadata);
            _logger.LogInformation("Upload tracked successfully: {FileId}", fileMetadata.FileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track upload: {FileId}", fileMetadata.FileId);
            throw;
        }
    }

    //
    // Records when a file is downloaded.
    // Useful for analytics, auditing, and detecting abuse.
    //
    public async Task TrackDownloadAsync(string fileId, string downloadedBy)
    {
        try
        {
            _logger.LogInformation("Tracking download: FileId={FileId}, DownloadedBy={DownloadedBy}", fileId, downloadedBy);
            await _dbContext.SaveAsync(new DownloadRecord
            {
                FileId = fileId,
                DownloadTime = DateTime.UtcNow,
                DownloadedBy = downloadedBy
            });
            _logger.LogInformation("Download tracked successfully: {FileId}", fileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track download: {FileId}", fileId);
            throw;
        }
    }

    //
    // Retrieves the most recent uploaded files.
    // Can be used for displaying upload history on the UI.
    //
    public async Task<List<FileMetadata>> GetRecentUploads(int count = 10)
    {
        try
        {
            _logger.LogInformation("Retrieving recent uploads: Count={Count}", count);
            var conditions = new List<ScanCondition>();
            var results = await _dbContext.ScanAsync<FileMetadata>(conditions)
                .GetRemainingAsync();

            var recentUploads = results.OrderByDescending(x => x.UploadTime).Take(count).ToList();
            _logger.LogInformation("Retrieved {ResultCount} recent uploads", recentUploads.Count);
            return recentUploads;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent uploads");
            throw;
        }
    }

    //
    /// Retrieves metadata for a specific file by ID.
    /// Used to check expiration and verify file existence.
    //
    public async Task<FileMetadata?> GetFileMetadataAsync(string fileId)
    {
        try
        {
            _logger.LogInformation("Retrieving file metadata: {FileId}", fileId);
            var metadata = await _dbContext.LoadAsync<FileMetadata>(fileId);
            if (metadata == null)
            {
                _logger.LogWarning("File metadata not found: {FileId}", fileId);
            }
            else
            {
                _logger.LogInformation("File metadata retrieved: {FileId}", fileId);
            }
            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve file metadata: {FileId}", fileId);
            throw;
        }
    }

    //
    // Removes file metadata from DynamoDB.
    // Should be called when a file is deleted to clean up records.
    //
    public async Task RemoveFileMetadataAsync(string fileId)
    {
        try
        {
            _logger.LogInformation("Removing file metadata: {FileId}", fileId);
            await _dbContext.DeleteAsync<FileMetadata>(fileId);
            _logger.LogInformation("File metadata removed successfully: {FileId}", fileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove file metadata: {FileId}", fileId);
            throw;
        }
    }
}

//
// Model for storing file upload metadata in DynamoDB.
// Primary key is FileId (the S3 object key).
// Used for tracking uploads and checking link expiration.
//
[DynamoDBTable("FileMetadata")]
public class FileMetadata
{
    // Primary key - the unique filename/key in S3 (GUID + extension).
    [DynamoDBHashKey]
    public required string FileId { get; set; }
    
    // Original filename provided by the user.
    public required string FileName { get; set; }
    
    // File size in bytes.
    public long FileSize { get; set; }
    
    // Type (e.g., "image/png", "image/jpeg").
    public required string ContentType { get; set; }
    
    // Timestamp of when the file was uploaded (UTC).
    public DateTime UploadTime { get; set; }
    
    // Who uploaded the file (currently "anonymous").
    public string? UploadedBy { get; set; }
}

//
// Model for recording download events for analytics and auditing.
// Tracks who downloaded what and when.
//
[DynamoDBTable("DownloadRecords")]
public class DownloadRecord
{
    // Primary key - the file that was downloaded.
    [DynamoDBHashKey]
    public string FileId { get; set; }
    
    // When the file was downloaded (UTC)
    public DateTime DownloadTime { get; set; }
    
    // Who triggered the download ("anonymous" or username).
    public string DownloadedBy { get; set; }
}
