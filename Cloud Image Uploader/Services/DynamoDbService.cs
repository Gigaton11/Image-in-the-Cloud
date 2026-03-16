using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Cloud_Image_Uploader.Models;

namespace Cloud_Image_Uploader.Services;

//
// Service for managing file metadata and download tracking in AWS DynamoDB.
// Stores upload information and download records for analytics and security.
//
public class DynamoDbService
{
    private readonly IDynamoDBContext _dbContext;
    private readonly ILogger<DynamoDbService> _logger;

    public DynamoDbService(IDynamoDBContext dbContext, ILogger<DynamoDbService> logger)
    {
        _logger = logger;
        _dbContext = dbContext;
        _logger.LogInformation("DynamoDbService initialized");
    }

    // Normalises a DateTime to UTC regardless of how the DynamoDB SDK deserialized it.
    // DynamoDB stores dates as strings; the SDK may return Unspecified kind on round-trip.
    public static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    // Returns the authoritative expiry time for a file in UTC.
    // Falls back to UploadTime + 10 minutes for legacy records that predate the ExpiresAtUtc field.
    public static DateTime GetExpirationUtc(FileMetadata fileMetadata)
    {
        if (fileMetadata.ExpiresAtUtc.HasValue)
        {
            return NormalizeUtc(fileMetadata.ExpiresAtUtc.Value);
        }

        return NormalizeUtc(fileMetadata.UploadTime).AddMinutes(10);
    }

    // Returns true when anyone (including unauthenticated users) may access the file.
    public static bool IsFilePublic(FileMetadata fileMetadata)
    {
        if (fileMetadata.IsPublic.HasValue)
        {
            return fileMetadata.IsPublic.Value;
        }

        // Backward compatibility: guest uploads were implicitly public.
        return string.IsNullOrWhiteSpace(fileMetadata.OwnerUserId);
    }

    // Persists a FileMetadata record after a successful S3 upload.
    public async Task TrackUploadAsync(FileMetadata fileMetadata)
    {
        try
        {
            _logger.LogInformation(
                "Tracking upload: FileId={FileId}, FileName={FileName}, Size={FileSizeBytes}, OwnerUserId={OwnerUserId}, ExpiresAtUtc={ExpiresAtUtc}",
                fileMetadata.FileId,
                fileMetadata.FileName,
                fileMetadata.FileSize,
                fileMetadata.OwnerUserId ?? "guest",
                GetExpirationUtc(fileMetadata));

            await _dbContext.SaveAsync(fileMetadata);
            _logger.LogInformation("Upload tracked successfully: {FileId}", fileMetadata.FileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track upload: {FileId}", fileMetadata.FileId);
            throw;
        }
    }

    // Records a download event in DownloadRecords for audit and analytics purposes.
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

    // Returns all non-expired files owned by the given user, newest first.
    // Uses a full-table scan filtered in memory because there is no owner GSI yet.
    public async Task<List<FileMetadata>> GetActiveUploadsForUserAsync(string ownerUserId, int maxCount = 100)
    {
        try
        {
            var conditions = new List<ScanCondition>();
            var results = await _dbContext.ScanAsync<FileMetadata>(conditions).GetRemainingAsync();
            var nowUtc = DateTime.UtcNow;

            return results
                .Where(x => string.Equals(x.OwnerUserId, ownerUserId, StringComparison.Ordinal))
                .Where(x => GetExpirationUtc(x) > nowUtc)
                .OrderByDescending(x => x.UploadTime)
                .Take(maxCount)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve active uploads for owner: {OwnerUserId}", ownerUserId);
            throw;
        }
    }

    // Loads a single FileMetadata record by its hash-key (FileId). Returns null when not found.
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

    // Deletes a FileMetadata record from DynamoDB. Called after S3 deletion so metadata is
    // removed only when the underlying objects are gone.
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

    // Overwrites an existing FileMetadata record (e.g. after a visibility toggle).
    public async Task UpdateFileMetadataAsync(FileMetadata fileMetadata)
    {
        try
        {
            await _dbContext.SaveAsync(fileMetadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update file metadata: {FileId}", fileMetadata.FileId);
            throw;
        }
    }

    // Returns up to maxCount file IDs whose expiry has already passed.
    // Used by the periodic cleanup service as a safety net for missed scheduled deletions.
    public async Task<List<string>> GetExpiredFileIdsAsync(int maxCount = 200)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            _logger.LogInformation("Scanning for expired files. NowUtc={NowUtc}", nowUtc);

            var conditions = new List<ScanCondition>();
            var allMetadata = await _dbContext.ScanAsync<FileMetadata>(conditions).GetRemainingAsync();

            var expiredFileIds = allMetadata
                .Where(x => GetExpirationUtc(x) <= nowUtc)
                .OrderBy(x => GetExpirationUtc(x))
                .Take(maxCount)
                .Select(x => x.FileId)
                .ToList();

            _logger.LogInformation("Expired file scan completed. Found={ExpiredCount}", expiredFileIds.Count);
            return expiredFileIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan expired files");
            throw;
        }
    }

    // Returns every FileMetadata record in the table.
    // Used only on startup to rehydrate per-file deletion timers.
    public async Task<List<FileMetadata>> GetAllFileMetadataAsync()
    {
        try
        {
            _logger.LogInformation("Retrieving all file metadata records");
            var conditions = new List<ScanCondition>();
            var results = await _dbContext.ScanAsync<FileMetadata>(conditions).GetRemainingAsync();
            _logger.LogInformation("Retrieved {Count} file metadata record(s)", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all file metadata records");
            throw;
        }
    }
}