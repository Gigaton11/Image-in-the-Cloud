using Amazon.S3;
using Cloud_Image_Uploader.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cloud_Image_Uploader.Controllers
{
    //
    // Main controller handling file upload, download, and image sharing operations.
    // Orchestrates S3Service for cloud storage and DynamoDbService for metadata tracking.
    // 
    public class HomeController : Controller
    {
        // Whitelist of allowed image extensions.
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        // Maximum upload size: 10 MB.
        private const long MaxUploadBytes = 10 * 1024 * 1024;

        // Service for managing AWS S3 operations.
        private readonly S3Service _s3Service;

        // Service for tracking uploads/downloads in DynamoDB.
        private readonly DynamoDbService _dynamoDbService;

        // Logger for HomeController operations.
        private readonly ILogger<HomeController> _logger;

        // Constructor with dependency injection of S3 and DynamoDB services.
        public HomeController(S3Service s3Service, DynamoDbService dynamoDbService, ILogger<HomeController> logger)
        {
            _s3Service = s3Service;
            _dynamoDbService = dynamoDbService;
            _logger = logger;
            _logger.LogInformation("HomeController initialized");
        }

        //GET / - Loads the main upload page.
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        //
        ///GET /ping - Health check endpoint to verify server is running.
        // Useful for monitoring and CI/CD pipelines.
        //
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok("Server is running");
        }

        //
        // POST / - Handles file upload.
        // Validates file (type, size), uploads to S3, tracks metadata in DynamoDB,
        // then returns a shareable link that expires in 10 minutes.
        //
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            // Validate file is present
            if (file == null)
            {
                _logger.LogWarning("Upload attempt with no file provided");
                TempData["Error"] = "Please select a file!";
                return View("Index");
            }

            // Validate file size (10 MB max)
            if (file.Length > MaxUploadBytes)
            {
                _logger.LogWarning("Upload attempt with oversized file: {FileName}, Size={FileSizeBytes}", file.FileName, file.Length);
                TempData["Error"] = "File too big! Max 10 MB allowed.";
                return View("Index");
            }

            // Validate file extension is in whitelist
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
            {
                TempData["Error"] = "Only JPG, PNG and WebP images are allowed.";
                return View("Index");
            }

            try
            {
                // Upload to S3 - returns the file key
                _logger.LogInformation("Processing file upload: {FileName}", file.FileName);
                string fileId = await _s3Service.UploadFileAsync(file);

                // Store metadata in DynamoDB for tracking and expiration checking
                await _dynamoDbService.TrackUploadAsync(new FileMetadata
                {
                    FileId = fileId,
                    FileName = file.FileName,
                    FileSize = file.Length,
                    ContentType = file.ContentType,
                    UploadTime = DateTime.UtcNow,
                    UploadedBy = "anonymous"
                });

                // Prepare UI data for the download page
                _logger.LogInformation("File upload completed successfully: {FileId}", fileId);
                TempData["Success"] = "Upload successful! Share this link (expires in 10 min):";
                TempData["ShareUrl"] = $"/download/{fileId}"; // Server-side link
                TempData["FileId"] = fileId;
                TempData["FileName"] = file.FileName;
                TempData["FileSize"] = (file.Length / 1024.0).ToString("F2") + " KB";
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File upload failed: {FileName}", file.FileName);
                TempData["Error"] = "Upload failed: " + ex.Message;
            }

            return View("Index");
        }

        //
        // GET /download/{fileId} - Secure file download with expiration checking.
        // This is a server-side endpoint that users click on instead of a direct S3 URL.
        // Ensures the link hasn't expired (10 minutes max), tracks the download, 
        // then streams the file from S3.
        // AWS credentials are never exposed to the browser.
        // Handles errors gracefully (file not found, expired link, etc.) and returns appropriate HTTP status codes.
        //
        [HttpGet("download/{fileName}")]
        public async Task<IActionResult> Download(string fileName)
        {
            try
            {
                _logger.LogInformation("Download request: {FileId}", fileName);

                // Retrieve metadata to check expiration and existence
                var metadata = await _dynamoDbService.GetFileMetadataAsync(fileName);
                if (metadata == null)
                {
                    _logger.LogWarning("Download request failed - metadata not found: {FileId}", fileName);
                    return NotFound("File metadata not found");
                }

                // Check if link has expired (10 minutes after upload)
                if (DateTime.UtcNow > metadata.UploadTime.AddMinutes(10))
                {
                    _logger.LogWarning("Download request failed - link expired: {FileId}", fileName);
                    return BadRequest("Share link has expired");
                }

                // Log the download for analytics
                var user = User.Identity?.Name ?? "anonymous";
                _logger.LogInformation("Download authorized: {FileId}, DownloadedBy={User}", fileName, user);
                await _dynamoDbService.TrackDownloadAsync(fileName, user);

                // Stream file from S3 - credentials are server-side only
                var stream = await _s3Service.DownloadFileAsync(fileName);
                return File(stream, "application/octet-stream", fileName);
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

        //
        // POST /delete/{fileId} - Securely delete a file.
        // Removes the file from S3 storage and metadata from DynamoDB.
        // This is restricted to files that haven't expired yet (10 minute window).
        // Useful for users who want to revoke access to shared files.
        //
        [HttpPost("delete/{fileId}")]
        public async Task<IActionResult> Delete(string fileId)
        {
            try
            {
                _logger.LogInformation("Delete request: {FileId}", fileId);

                // Retrieve metadata to verify file exists and check if still valid
                var metadata = await _dynamoDbService.GetFileMetadataAsync(fileId);
                if (metadata == null)
                {
                    _logger.LogWarning("Delete request failed - metadata not found: {FileId}", fileId);
                    TempData["Error"] = "File not found. May have already expired.";
                    return View("Index");
                }

                // Check if link has expired
                if (DateTime.UtcNow > metadata.UploadTime.AddMinutes(10))
                {
                    _logger.LogWarning("Delete request failed - link expired: {FileId}", fileId);
                    TempData["Error"] = "File has already expired. Cannot delete.";
                    return View("Index");
                }

                // Delete file from S3
                _logger.LogInformation("Deleting file from S3: {FileId}", fileId);
                await _s3Service.DeleteFileAsync(fileId);

                // Remove metadata from DynamoDB
                _logger.LogInformation("Removing metadata from DynamoDB: {FileId}", fileId);
                await _dynamoDbService.RemoveFileMetadataAsync(fileId);

                _logger.LogInformation("File deleted successfully: {FileId}", fileId);
                TempData["Success"] = "File deleted successfully! Access revoked for all shares.";
                return View("Index");
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Delete failed - S3 error: {FileId}", fileId);
                TempData["Error"] = $"Delete failed (S3 error): {ex.Message}";
                return View("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete failed - internal error: {FileId}", fileId);
                TempData["Error"] = $"Delete failed: {ex.Message}";
                return View("Index");
            }
        }


    }
}
