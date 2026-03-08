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
        if (!_scheduled.TryAdd(fileId, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Scheduled deletion for {FileId} in {DelayMinutes} minute(s)", fileId, delay.TotalMinutes);
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
        using var scope = _scopeFactory.CreateScope();
        var s3Service = scope.ServiceProvider.GetRequiredService<S3Service>();
        var dynamoDbService = scope.ServiceProvider.GetRequiredService<DynamoDbService>();

        try
        {
            await s3Service.DeleteFileAsync(fileId);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("File already deleted in S3: {FileId}", fileId);
        }

        await dynamoDbService.RemoveFileMetadataAsync(fileId);
        _logger.LogInformation("Scheduled cleanup completed for {FileId}", fileId);
    }
}
