using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace Cloud_Image_Uploader.Services;

public class S3Service
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    private readonly string _bucketName;
    private readonly IAmazonS3 _s3Client;
    private readonly TransferUtility _transferUtility;

    public S3Service(IConfiguration config, IAmazonS3 s3Client)
    {
        _bucketName = config["AWS:BucketName"]
            ?? throw new InvalidOperationException("AWS bucket name is not configured.");
        _s3Client = s3Client;
        _transferUtility = new TransferUtility(_s3Client);
    }

    // Uploads a file and returns the file key.
    public async Task<string> UploadFileAsync(IFormFile file)
    {
        ValidateUpload(file);
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

        using var stream = file.OpenReadStream();
        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = stream,
            Key = fileName,
            BucketName = _bucketName,
            ContentType = file.ContentType
        };

        await _transferUtility.UploadAsync(uploadRequest);
        return fileName;
    }

    public async Task<Stream> DownloadFileAsync(string key)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        var response = await _s3Client.GetObjectAsync(request);
        return response.ResponseStream;
    }

    public async Task DeleteFileAsync(string key)
    {
        await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        });
    }

    private static void ValidateUpload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty.");

        if (file.Length > MaxFileSizeBytes)
            throw new ArgumentException("File too large.");

        // Keep service-level validation even if controller validates too.
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("File is not a valid image format.");
    }

    public string GetPreSignedUrl(string key)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddMinutes(10),
        };

        return _s3Client.GetPreSignedURL(request);
    }

    public static string MaskUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        // Remove query parameters (contain credentials and signatures)
        var baseUrl = url.Split('?')[0];
        
        // Extract just the filename from the path
        var fileName = Path.GetFileName(baseUrl);
        
        return fileName;
    }
}
