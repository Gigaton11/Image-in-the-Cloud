using Amazon.S3;
using System.Collections.Concurrent;
using System.Net;

namespace Cloud_Image_Uploader.Services;

//
// Schedules per-file deletion to run 10 minutes after upload.
// This avoids relying only on periodic scans for expiration cleanup.
//
public class FileDeletionSchedulerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FileDeletionSchedulerService> _logger;
    private readonly ConcurrentDictionary<string, byte> _scheduled = new(StringComparer.Ordinal);

    public FileDeletionSchedulerService(
        IServiceScopeFactory scopeFactory,
        ILogger<FileDeletionSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void ScheduleDelete(string fileId, TimeSpan delay)
    {
        // Prevent duplicate timers for the same file key.
        if (!_scheduled.TryAdd(fileId, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Scheduled deletion for {FileId} in {DelayMinutes} minute(s)", fileId, delay.TotalMinutes);
                // On restart recovery, delay can be <= 0, so delete immediately.
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay);
                }
                await DeleteFileAndMetadataAsync(fileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled delete failed for {FileId}", fileId);
            }
            finally
            {
                _scheduled.TryRemove(fileId, out _);
            }
        });
    }

    public async Task DeleteFileAndMetadataAsync(string fileId)
    {
        // Resolve scoped services here so this method is safe from background tasks.
        using var scope = _scopeFactory.CreateScope();
        var s3Service = scope.ServiceProvider.GetRequiredService<S3Service>();
        var dynamoDbService = scope.ServiceProvider.GetRequiredService<DynamoDbService>();
        var metadata = await dynamoDbService.GetFileMetadataAsync(fileId);

        try
        {
            // Delete the processed web-optimized version
            await s3Service.DeleteFileAsync($"{fileId}_web.webp");
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Web-optimized image already deleted in S3: {FileId}", fileId);
        }

        try
        {
            // Delete the processed thumbnail version
            await s3Service.DeleteFileAsync($"{fileId}_thumb.webp");
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Thumbnail image already deleted in S3: {FileId}", fileId);
        }

        if (metadata != null)
        {
            try
            {
                // Delete the original uploaded version
                var originalFileKey = S3Service.BuildOriginalFileKey(fileId, metadata.FileName);
                await s3Service.DeleteFileAsync(originalFileKey);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Original image already deleted in S3: {FileId}", fileId);
            }
        }

        await dynamoDbService.RemoveFileMetadataAsync(fileId);
        _logger.LogInformation("Scheduled cleanup completed for {FileId}", fileId);
    }
}
