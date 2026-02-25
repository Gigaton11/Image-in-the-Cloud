using Amazon.S3;
using Cloud_Image_Uploader.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cloud_Image_Uploader.Controllers
{

    public class HomeController : Controller
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        private const long MaxUploadBytes = 10 * 1024 * 1024;
        private readonly S3Service _s3Service;
        private readonly DynamoDbService _dynamoDbService;

        public HomeController(S3Service s3Service, DynamoDbService dynamoDbService)
        {
            _s3Service = s3Service;
            _dynamoDbService = dynamoDbService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok("Server is running");
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null)
            {
                TempData["Error"] = "Please select a file!";
                return View("Index");
            }

            if (file.Length > MaxUploadBytes)
            {
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
                string url = await _s3Service.UploadFileAsync(file);
                // The generated S3 object key is encoded in the signed URL path.
                var fileId = Path.GetFileName(new Uri(url).AbsolutePath);

                // Persist upload metadata for later analytics/auditing.
                await _dynamoDbService.TrackUploadAsync(new FileMetadata
                {
                    FileId = fileId,
                    FileName = file.FileName,
                    FileSize = file.Length,
                    ContentType = file.ContentType,
                    UploadTime = DateTime.UtcNow,
                    UploadedBy = "anonymous"
                });

                TempData["Success"] = "Upload successful! Share this link (expires in 10 min):";
                TempData["ImageUrl"] = url;
                TempData["FileId"] = fileId;
                TempData["FileName"] = file.FileName;
                TempData["FileSize"] = (file.Length / 1024.0).ToString("F2") + " KB";
                
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Upload failed: " + ex.Message;
            }

            return View("Index");
        }


        [HttpGet("download/{fileName}")]
        public async Task<IActionResult> Download(string fileName)
        {
            try
            {
                // Track download (example: use email from claims or "anonymous")
                var user = User.Identity?.Name ?? "anonymous";
                await _dynamoDbService.TrackDownloadAsync(fileName, user);

                var stream = await _s3Service.DownloadFileAsync(fileName);
                return File(stream, "application/octet-stream", fileName);
            }
            catch (AmazonS3Exception ex)
            {
                return NotFound($"File not found: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
