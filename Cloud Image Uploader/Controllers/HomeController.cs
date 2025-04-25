using System.Diagnostics;
using Cloud_Image_Uploader.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Cloud_Image_Uploader.Services;

namespace Cloud_Image_Uploader.Controllers
{

    public class HomeController : Controller
    {
        private readonly S3Service _s3Service;

        public HomeController(S3Service s3Service)
        {
            _s3Service = s3Service;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file != null && file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    string url = await _s3Service.UploadFileAsync(file);
                    ViewBag.ImageUrl = url;
                }
            }
            return View("Index");
        }
    }
}