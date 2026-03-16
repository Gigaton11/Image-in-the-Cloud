using System.Security.Claims;
using Amazon.S3;
using Cloud_Image_Uploader.Models;
using Cloud_Image_Uploader.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cloud_Image_Uploader.Controllers;

//
// Primary controller: handles image uploads, downloads, thumbnail serving,
// user-initiated deletion, and visibility toggling.
// Guest uploads are always public and use the shortest retention window;
// authenticated uploads support configurable retention and private/public visibility.
//
public class HomeController : Controller
{
    // Accepted image file extensions — validated at the controller boundary before
    // the file is passed to S3Service (which re-validates as defense-in-depth).
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private const long MaxUploadBytes = 10 * 1024 * 1024; // 10 MB hard limit enforced before processing

    // String constants for the retention and visibility form values sent from the UI.
    private const string DefaultRetentionOption = "10m";
    private const string OneHourRetentionOption = "1h";
    private const string SixHourRetentionOption = "6h";
    private const string ExtendedRetentionOption = "1d";
    private const string DefaultVisibilityOption = "private";
    private const string PublicVisibilityOption = "public";

    private static readonly TimeSpan GuestRetention = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OneHourRetention = TimeSpan.FromHours(1);
    private static readonly TimeSpan SixHourRetention = TimeSpan.FromHours(6);
    private static readonly TimeSpan ExtendedRetention = TimeSpan.FromDays(1);

    private readonly S3Service _s3Service;
    private readonly DynamoDbService _dynamoDbService;
    private readonly ImageProcessingService _imageProcessingService;
    private readonly ILogger<HomeController> _logger;
    private readonly FileDeletionSchedulerService _fileDeletionSchedulerService;

    public HomeController(
        S3Service s3Service,
        DynamoDbService dynamoDbService,
        ImageProcessingService imageProcessingService,
        FileDeletionSchedulerService fileDeletionSchedulerService,
        ILogger<HomeController> logger)
    {
        _s3Service = s3Service;
        _dynamoDbService = dynamoDbService;
        _imageProcessingService = imageProcessingService;
        _fileDeletionSchedulerService = fileDeletionSchedulerService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [Authorize]
    [HttpGet("my-images")]
    public async Task<IActionResult> MyImages()
    {
        var currentUserId = GetCurrentUserId();
        // [Authorize] should prevent reaching here unauthenticated, but guard
        // defensively in case the NameIdentifier claim is absent after cookie
        // auth is re-issued without a full sign-out.
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Challenge();
        }

        var files = await _dynamoDbService.GetActiveUploadsForUserAsync(currentUserId);
        // Project storage records into display-ready view models here so the
        // view stays free of formatting and business logic.
        var model = files.Select(file =>
        {
            var expiresAtUtc = DynamoDbService.GetExpirationUtc(file);
            var isPublic = DynamoDbService.IsFilePublic(file);
            return new OwnedImageViewModel
            {
                FileId = file.FileId,
                FileName = file.FileName,
                ThumbnailUrl = $"/thumbnail/{file.FileId}",
                DownloadUrl = $"/download/{file.FileId}",
                ShareUrl = $"/download/{file.FileId}",
                UploadedAtText = DynamoDbService.NormalizeUtc(file.UploadTime).ToLocalTime().ToString("g"),
                ExpiresAtText = expiresAtUtc.ToLocalTime().ToString("g"),
                ExpiresInText = FormatRemainingTime(expiresAtUtc - DateTime.UtcNow),
                FileSizeText = $"{file.FileSize / 1024.0:F1} KB",
                IsPublic = isPublic,
                VisibilityLabel = isPublic ? "Public" : "Private",
                ToggleVisibilityOption = isPublic ? "private" : "public",
                ToggleVisibilityText = isPublic ? "Make Private" : "Make Public"
            };
        }).ToList();

        return View(model);
    }

    // GET /ping — lightweight liveness probe for uptime monitors and manual checks.
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok("Server is running");
    }

    // POST /Home/Upload — validates the file, generates web/thumbnail variants via
    // ImageProcessingService, uploads all variants to S3, persists metadata in DynamoDB,
    // and schedules an in-process expiry timer. Re-renders Index on both success and failure.
    [HttpPost]
    public async Task<IActionResult> Upload(
        IFormFile file,
        string retentionOption = DefaultRetentionOption,
        string visibilityOption = DefaultVisibilityOption)
    {
        if (file == null)
        {
            _logger.LogWarning("Upload attempt with no file provided");
            TempData["Error"] = "Please select a file!";
            return View("Index");
        }

        if (file.Length > MaxUploadBytes)
        {
            _logger.LogWarning("Upload attempt with oversized file: {FileName}, Size={FileSizeBytes}", file.FileName, file.Length);
            TempData["Error"] = "File too big! Max 10 MB allowed.";
            return View("Index");
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            TempData["Error"] = "Only JPG, PNG and WebP images are allowed.";
            return View("Index");
        }

        try
        {
            _logger.LogInformation("Processing file upload: {FileName}", file.FileName);

            var processedImages = await _imageProcessingService.ProcessImageAsync(file);
            var fileId = await _s3Service.UploadProcessedImagesAsync(file, processedImages);

            var uploadTimeUtc = DateTime.UtcNow;
            var retention = ResolveRetentionDuration(User.Identity?.IsAuthenticated == true, retentionOption);
            var expiresAtUtc = uploadTimeUtc.Add(retention);
            var currentUserId = GetCurrentUserId();
            var uploadedBy = User.Identity?.IsAuthenticated == true ? User.Identity?.Name ?? "user" : "anonymous";
            var isPublic = ResolveVisibility(User.Identity?.IsAuthenticated == true, visibilityOption);

            // Persist ownership/expiry metadata so access checks and cleanup jobs use the same source of truth.
            await _dynamoDbService.TrackUploadAsync(new FileMetadata
            {
                FileId = fileId,
                FileName = file.FileName,
                FileSize = file.Length,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                UploadTime = uploadTimeUtc,
                ExpiresAtUtc = expiresAtUtc,
                UploadedBy = uploadedBy,
                OwnerUserId = User.Identity?.IsAuthenticated == true ? currentUserId : null,
                IsPublic = isPublic
            });

            // Schedule eager in-process deletion; the background cleanup service remains the safety net.
            _fileDeletionSchedulerService.ScheduleDelete(fileId, expiresAtUtc - DateTime.UtcNow);

            _logger.LogInformation(
                "File upload completed successfully: {FileId}, Original: {OriginalSize}KB, Web: {WebSize}KB, Thumb: {ThumbSize}KB, Owner={Owner}",
                fileId,
                processedImages.OriginalSizeBytes / 1024.0,
                processedImages.WebFormatSizeBytes / 1024.0,
                processedImages.ThumbnailSizeBytes / 1024.0,
                currentUserId ?? "guest");

            TempData["Success"] = "Upload successful!";
            TempData["ShareUrl"] = $"/download/{fileId}";
            TempData["ThumbnailUrl"] = $"/thumbnail/{fileId}";
            TempData["FileId"] = fileId;
            TempData["FileName"] = file.FileName;
            TempData["FileSize"] = (file.Length / 1024.0).ToString("F2") + " KB";
            TempData["WebSize"] = (processedImages.WebFormatSizeBytes / 1024.0).ToString("F2") + " KB";
            TempData["CompressionPercent"] = Math.Round((1 - (double)processedImages.WebFormatSizeBytes / processedImages.OriginalSizeBytes) * 100) + "%";
            TempData["ExpiresInText"] = GetRetentionDisplayText(retention);
            TempData["ExpiresAtText"] = expiresAtUtc.ToLocalTime().ToString("g");
            TempData["VisibilityText"] = isPublic ? "Public" : "Private";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File upload failed: {FileName}", file.FileName);
            TempData["Error"] = "Upload failed: " + ex.Message;
        }

        return View("Index");
    }

    // GET /download/{fileName} — streams the original uploaded file to the browser.
    // Enforces visibility and expiry before serving; falls back to the web variant
    // for files uploaded before the original-preservation feature was added.
    [HttpGet("download/{fileName}")]
    public async Task<IActionResult> Download(string fileName)
    {
        try
        {
            _logger.LogInformation("Download request: {FileId}", fileName);

            var metadata = await _dynamoDbService.GetFileMetadataAsync(fileName);
            if (metadata == null)
            {
                _logger.LogWarning("Download request failed - metadata not found: {FileId}", fileName);
                return NotFound("File metadata not found");
            }

            if (!CanCurrentUserView(metadata))
            {
                _logger.LogWarning("Download request denied for private image {FileId}", fileName);
                return Forbid();
            }

            var expiresAtUtc = DynamoDbService.GetExpirationUtc(metadata);
            if (DateTime.UtcNow > expiresAtUtc)
            {
                _logger.LogWarning("Download request failed - link expired: {FileId}", fileName);
                // Trigger cleanup on access so expired files don't linger if the
                // background service hasn't run yet (e.g. after an app restart).
                await _fileDeletionSchedulerService.DeleteFileAndMetadataAsync(fileName);
                return BadRequest("Share link has expired");
            }

            var user = User.Identity?.Name ?? "anonymous";
            _logger.LogInformation("Download authorized: {FileId}, DownloadedBy={User}", fileName, user);
            // Record every download regardless of authentication state — this
            // log is used for analytics and helps audit accidental public shares.
            await _dynamoDbService.TrackDownloadAsync(fileName, user);

            var originalFileKey = S3Service.BuildOriginalFileKey(fileName, metadata.FileName);
            try
            {
                var stream = await _s3Service.DownloadFileAsync(originalFileKey);
                return File(stream, metadata.ContentType, metadata.FileName);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Original variant not found for {FileId}. Falling back to web variant.", fileName);
                // Older records may only have optimized assets, so keep the download working with a web variant fallback.
                var webFileName = $"{fileName}_web.webp";
                var fallbackName = $"{Path.GetFileNameWithoutExtension(metadata.FileName)}.webp";
                var fallbackStream = await _s3Service.DownloadFileAsync(webFileName);
                return File(fallbackStream, "image/webp", fallbackName);
            }
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "Download failed - S3 error: {FileId}", fileName);
            return NotFound($"File not found: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed - internal error: {FileId}", fileName);
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    // GET /thumbnail/{fileId} — serves the 220×220 WebP thumbnail.
    // Applies the same visibility and expiry checks as the download endpoint.
    [HttpGet("thumbnail/{fileId}")]
    public async Task<IActionResult> Thumbnail(string fileId)
    {
        try
        {
            _logger.LogInformation("Thumbnail request: {FileId}", fileId);

            var metadata = await _dynamoDbService.GetFileMetadataAsync(fileId);
            if (metadata == null)
            {
                _logger.LogWarning("Thumbnail request failed - metadata not found: {FileId}", fileId);
                return NotFound("File metadata not found");
            }

            if (!CanCurrentUserView(metadata))
            {
                _logger.LogWarning("Thumbnail request denied for private image {FileId}", fileId);
                return Forbid();
            }

            var expiresAtUtc = DynamoDbService.GetExpirationUtc(metadata);
            if (DateTime.UtcNow > expiresAtUtc)
            {
                _logger.LogWarning("Thumbnail request failed - link expired: {FileId}", fileId);
                await _fileDeletionSchedulerService.DeleteFileAndMetadataAsync(fileId);
                return NotFound("Thumbnail has expired");
            }

            var thumbnailFileName = $"{fileId}_thumb.webp";
            var stream = await _s3Service.DownloadFileAsync(thumbnailFileName);
            return File(stream, "image/webp");
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "Thumbnail request failed - S3 error: {FileId}", fileId);
            return NotFound($"Thumbnail not found: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Thumbnail request failed - internal error: {FileId}", fileId);
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    // POST /delete/{fileId} — user-initiated deletion. Removes all three S3 variants
    // (web, thumbnail, original) independently so a missing variant cannot block the
    // others, then removes the DynamoDB metadata record.
    // Available to guests (for their own expiring uploads) and authenticated owners.
    [HttpPost("delete/{fileId}")]
    public async Task<IActionResult> Delete(string fileId)
    {
        try
        {
            _logger.LogInformation("Delete request: {FileId}", fileId);

            var metadata = await _dynamoDbService.GetFileMetadataAsync(fileId);
            if (metadata == null)
            {
                _logger.LogWarning("Delete request failed - metadata not found: {FileId}", fileId);
                TempData["Error"] = "File not found. May have already expired.";
                return RedirectToRefererOrIndex();
            }

            if (!CanCurrentUserManage(metadata))
            {
                _logger.LogWarning("Delete request denied for file {FileId} owned by {OwnerUserId}", fileId, metadata.OwnerUserId);
                TempData["Error"] = "You can only manage images owned by your account.";
                return RedirectToRefererOrIndex();
            }

            // Each S3 variant is deleted in its own try/catch so a missing variant
            // (e.g. from a partial upload) does not abort the rest of the cleanup.
            try
            {
                _logger.LogInformation("Deleting web-optimized image from S3: {FileId}", fileId);
                await _s3Service.DeleteFileAsync($"{fileId}_web.webp");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete web version: {FileId}", fileId);
            }

            try
            {
                _logger.LogInformation("Deleting thumbnail from S3: {FileId}", fileId);
                await _s3Service.DeleteFileAsync($"{fileId}_thumb.webp");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thumbnail version: {FileId}", fileId);
            }

            try
            {
                var originalFileKey = S3Service.BuildOriginalFileKey(fileId, metadata.FileName);
                _logger.LogInformation("Deleting original image from S3: {FileId}", fileId);
                await _s3Service.DeleteFileAsync(originalFileKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete original version: {FileId}", fileId);
            }

            _logger.LogInformation("Removing metadata from DynamoDB: {FileId}", fileId);
            await _dynamoDbService.RemoveFileMetadataAsync(fileId);

            _logger.LogInformation("File deleted successfully: {FileId}", fileId);
            TempData["Success"] = "File deleted successfully! Access revoked for all shares.";
            return RedirectToRefererOrIndex();
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "Delete failed - S3 error: {FileId}", fileId);
            TempData["Error"] = $"Delete failed (S3 error): {ex.Message}";
            return RedirectToRefererOrIndex();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete failed - internal error: {FileId}", fileId);
            TempData["Error"] = $"Delete failed: {ex.Message}";
            return RedirectToRefererOrIndex();
        }
    }

    // POST /visibility/{fileId} — toggles a file between public and private.
    // Requires authentication and ownership. Redirects back to the optional returnUrl
    // (validated as local) or falls back to My Images.
    [Authorize]
    [HttpPost("visibility/{fileId}")]
    public async Task<IActionResult> UpdateVisibility(string fileId, string visibilityOption, string? returnUrl = null)
    {
        var metadata = await _dynamoDbService.GetFileMetadataAsync(fileId);
        if (metadata == null)
        {
            TempData["Error"] = "File not found.";
            return RedirectToAction(nameof(MyImages));
        }

        if (!CanCurrentUserManage(metadata))
        {
            TempData["Error"] = "You can only change visibility for your own images.";
            return RedirectToAction(nameof(MyImages));
        }

        metadata.IsPublic = string.Equals(visibilityOption, PublicVisibilityOption, StringComparison.OrdinalIgnoreCase);
        await _dynamoDbService.UpdateFileMetadataAsync(metadata);

        TempData["Success"] = metadata.IsPublic == true
            ? "Image is now public. Share link works for anyone."
            : "Image is now private. Only you can access it while logged in.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(MyImages));
    }

    // Returns the NameIdentifier claim value, which is the normalised (upper-cased)
    // username set during sign-in. Null when the user is not authenticated.
    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private bool CanCurrentUserManage(FileMetadata metadata)
    {
        // Guest uploads carry no owner claim, so the file ID itself is the
        // access token — anyone with the ID can delete the ephemeral file.
        if (string.IsNullOrWhiteSpace(metadata.OwnerUserId))
        {
            return true;
        }

        var currentUserId = GetCurrentUserId();
        return !string.IsNullOrWhiteSpace(currentUserId)
            && string.Equals(metadata.OwnerUserId, currentUserId, StringComparison.Ordinal);
    }

    private bool CanCurrentUserView(FileMetadata metadata)
    {
        // Public files are accessible to everyone. Files without an owner are
        // guest uploads — they're inherently public even if the flag isn't set.
        if (DynamoDbService.IsFilePublic(metadata) || string.IsNullOrWhiteSpace(metadata.OwnerUserId))
        {
            return true;
        }

        var currentUserId = GetCurrentUserId();
        return !string.IsNullOrWhiteSpace(currentUserId)
            && string.Equals(metadata.OwnerUserId, currentUserId, StringComparison.Ordinal);
    }

    private static TimeSpan ResolveRetentionDuration(bool isAuthenticated, string? retentionOption)
    {
        // Guests always use the shortest retention window; custom options are reserved for signed-in users.
        if (!isAuthenticated)
        {
            return GuestRetention;
        }

        if (string.Equals(retentionOption, OneHourRetentionOption, StringComparison.OrdinalIgnoreCase))
        {
            return OneHourRetention;
        }

        if (string.Equals(retentionOption, SixHourRetentionOption, StringComparison.OrdinalIgnoreCase))
        {
            return SixHourRetention;
        }

        if (string.Equals(retentionOption, ExtendedRetentionOption, StringComparison.OrdinalIgnoreCase))
        {
            return ExtendedRetention;
        }

        return GuestRetention;
    }

    // Returns a human-readable label for the retention period shown after upload.
    private static string GetRetentionDisplayText(TimeSpan retention)
    {
        if (retention == OneHourRetention)
        {
            return "1 hour";
        }

        if (retention == SixHourRetention)
        {
            return "6 hours";
        }

        if (retention == ExtendedRetention)
        {
            return "1 day";
        }

        return "10 minutes";
    }

    private static bool ResolveVisibility(bool isAuthenticated, string? visibilityOption)
    {
        // Guest uploads are always public — there is no user session to enforce
        // private access against when the file is later requested.
        if (!isAuthenticated)
        {
            return true;
        }

        return string.Equals(visibilityOption, PublicVisibilityOption, StringComparison.OrdinalIgnoreCase);
    }

    // Formats a countdown TimeSpan into a short display string for the My Images table.
    private static string FormatRemainingTime(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "Expired";
        }

        if (remaining.TotalHours >= 24)
        {
            return $"{Math.Ceiling(remaining.TotalDays)} day(s)";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        }

        return $"{Math.Max(0, (int)remaining.TotalMinutes)}m {Math.Max(0, remaining.Seconds)}s";
    }

    private IActionResult RedirectToRefererOrIndex()
    {
        var referer = Request.Headers.Referer.ToString();
        // Restrict redirects to the current host to avoid open redirect behavior.
        if (!string.IsNullOrWhiteSpace(referer)
            && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri)
            && string.Equals(refererUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return LocalRedirect(refererUri.PathAndQuery);
        }

        return RedirectToAction(nameof(Index));
    }
}
