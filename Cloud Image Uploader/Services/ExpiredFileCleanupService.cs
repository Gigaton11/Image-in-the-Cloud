namespace Cloud_Image_Uploader.Services;

//
// Background worker that deletes files from S3 after their individual expiry time
// and removes their metadata from DynamoDB.
//
public class ExpiredFileCleanupService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FileDeletionSchedulerService _fileDeletionSchedulerService;
    private readonly ILogger<ExpiredFileCleanupService> _logger;
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);

    public ExpiredFileCleanupService(
        IServiceScopeFactory scopeFactory,
        FileDeletionSchedulerService fileDeletionSchedulerService,
        ILogger<ExpiredFileCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _fileDeletionSchedulerService = fileDeletionSchedulerService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Expired file cleanup worker started");
        // First pass after restart: catch up overdue files and re-schedule pending ones.
        await RehydrateSchedulesAndCleanupExpiredAsync(stoppingToken);

        using var timer = new PeriodicTimer(ScanInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredFilesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expired file cleanup cycle failed");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Expired file cleanup worker stopped");
    }

    // Triggers a one-off cleanup pass, useful for external schedulers when instances scale to zero.
    public async Task<int> RunCleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Manual expired file cleanup requested");
        var deletedCount = await CleanupExpiredFilesAsync(cancellationToken);
        _logger.LogInformation("Manual expired file cleanup finished. Deleted={DeletedCount}", deletedCount);
        return deletedCount;
    }

    private async Task<int> CleanupExpiredFilesAsync(CancellationToken cancellationToken)
    {
        await _cleanupGate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dynamoDbService = scope.ServiceProvider.GetRequiredService<DynamoDbService>();

            // Safety net in case per-file scheduled task was missed.
            var expiredFileIds = await dynamoDbService.GetExpiredFileIdsAsync();
            if (expiredFileIds.Count == 0)
            {
                return 0;
            }

            _logger.LogInformation("Deleting {Count} expired file(s)", expiredFileIds.Count);
            var deletedCount = 0;

            foreach (var fileId in expiredFileIds)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await _fileDeletionSchedulerService.DeleteFileAndMetadataAsync(fileId);
                    deletedCount++;
                    _logger.LogInformation("Expired file deleted successfully: {FileId}", fileId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete expired file: {FileId}", fileId);
                }
            }

            return deletedCount;
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private async Task RehydrateSchedulesAndCleanupExpiredAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dynamoDbService = scope.ServiceProvider.GetRequiredService<DynamoDbService>();
        var allFiles = await dynamoDbService.GetAllFileMetadataAsync();

        var nowUtc = DateTime.UtcNow;
        var scheduledCount = 0;
        var deletedCount = 0;

        foreach (var file in allFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var expiresAtUtc = DynamoDbService.GetExpirationUtc(file);
            if (expiresAtUtc <= nowUtc)
            {
                try
                {
                    await _fileDeletionSchedulerService.DeleteFileAndMetadataAsync(file.FileId);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Startup cleanup failed for expired file: {FileId}", file.FileId);
                }

                continue;
            }

            var remaining = expiresAtUtc - nowUtc;
            _fileDeletionSchedulerService.ScheduleDelete(file.FileId, remaining);
            scheduledCount++;
        }

        _logger.LogInformation(
            "Startup deletion recovery complete. Scheduled={ScheduledCount}, DeletedNow={DeletedCount}",
            scheduledCount,
            deletedCount);
    }
}
