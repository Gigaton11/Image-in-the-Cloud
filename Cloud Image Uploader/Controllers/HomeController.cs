using Amazon.S3;
using Microsoft.AspNetCore.Mvc;

namespace Cloud_Image_Uploader.Controllers
{

    public class HomeController : Controller
    {
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

            if (file.Length > 10 * 1024 * 1024)
            {
                TempData["Error"] = "File too big! Max 10 MB allowed.";
                return View("Index");
            }

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
            {
                TempData["Error"] = "Only JPG, PNG and WebP images are allowed.";
                return View("Index");
            }

            try
            {
                string url = await _s3Service.UploadFileAsync(file);
                var fileId = Path.GetFileName(url.Split('?')[0]);

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
                TempData["FileSize"] = file.Length;
                
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