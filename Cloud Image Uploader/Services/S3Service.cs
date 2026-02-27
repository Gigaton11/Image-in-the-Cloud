using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace Cloud_Image_Uploader.Services;

//
// Service for handling AWS S3 operations including file upload, download, and deletion.
// Manages AWS S3 bucket interactions and generates secure pre-signed URLs for temporary access.
//
public class S3Service
{
    //
    // Whitelist of allowed image file extensions.
    // Only these formats are permitted to prevent arbitrary file uploads.
    //
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    //
    // Maximum file size allowed: 10 MB.
    // Prevents large file uploads that could consume excessive storage and bandwidth.
    //
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    // S3 bucket name from configuration.
    private readonly string _bucketName;

    // AWS S3 client for API interactions.
    private readonly IAmazonS3 _s3Client;

    // Utility for managing multi-part uploads and downloads.
    private readonly TransferUtility _transferUtility;

    // Logger for S3Service operations.
    private readonly ILogger<S3Service> _logger;

    //
    // Constructor that initializes the S3 service with AWS credentials and configuration.
    // Throws if bucket name is not configured in appsettings.json.
    // AWS credentials are injected via dependency injection from Program.cs.
    //
    public S3Service(IConfiguration config, IAmazonS3 s3Client, ILogger<S3Service> logger)
    {
        _logger = logger;
        _bucketName = config["AWS:BucketName"]
            ?? throw new InvalidOperationException("AWS bucket name is not configured.");
        _s3Client = s3Client;
        _transferUtility = new TransferUtility(_s3Client);
        _logger.LogInformation("S3Service initialized with bucket: {BucketName}", _bucketName);
    }

    //
    // Uploads a file to S3 bucket after validation.
    // Uses a GUID to generate a unique filename, preventing collisions.
    // Returns only the file key (not the full signed URL) for security.
    //

    public async Task<string> UploadFileAsync(IFormFile file)
    {
        ValidateUpload(file);
        // Generate unique filename: [GUID].[extension]
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

        _logger.LogInformation("Starting file upload: OriginalName={OriginalName}, Size={FileSizeKB}KB, GeneratedName={GeneratedName}", 
            file.FileName, file.Length / 1024.0, fileName);

        using var stream = file.OpenReadStream();
        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = stream,
            Key = fileName,
            BucketName = _bucketName,
            ContentType = file.ContentType
        };

        try
        {
            await _transferUtility.UploadAsync(uploadRequest);
            _logger.LogInformation("File uploaded successfully: {FileKey}", fileName);
            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File upload failed for: {FileKey}", fileName);
            throw;
        }
    }

    //
    // Retrieves a file from S3 as a stream for downloading.
    // Called by the Download endpoint to serve files to users.
    //
    public async Task<Stream> DownloadFileAsync(string key)
    {
        _logger.LogInformation("Starting file download: {FileKey}", key);

        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        try
        {
            var response = await _s3Client.GetObjectAsync(request);
            _logger.LogInformation("File downloaded successfully: {FileKey}", key);
            return response.ResponseStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File download failed for: {FileKey}", key);
            throw;
        }
    }

    // TODO: Implement file deletion method for cleanup of expired or unwanted files.
    // Deletes a file from the S3 bucket.
    // Can be used to remove expired or unwanted files.
    //
    public async Task DeleteFileAsync(string key)
    {
        _logger.LogInformation("Starting file deletion: {FileKey}", key);

        try
        {
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            });
            _logger.LogInformation("File deleted successfully: {FileKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File deletion failed for: {FileKey}", key);
            throw;
        }
    }

    //
    // Validates a file before upload.
    // Checks: file exists, size limit, and file type.
    // Multiple layers of validation (controller + service) provide defense-in-depth.
    // 
    private void ValidateUpload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Upload validation failed: File is empty");
            throw new ArgumentException("File is empty.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            _logger.LogWarning("Upload validation failed: File too large. Size={FileSizeBytes}", file.Length);
            throw new ArgumentException("File too large.");
        }

        // Validate file extension is allowed (defense-in-depth)
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            _logger.LogWarning("Upload validation failed: Invalid file extension. Extension={Extension}", extension);
            throw new ArgumentException("File is not a valid image format.");
        }
    }

    //
    // Generates a temporary, signed URL for accessing a file in S3.
    // The URL expires in 10 minutes and includes AWS authentication credentials.
    // This is called server-side only and returned to the browser via redirect.
    //
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

    //
    // Masks/obscures a signed URL to hide AWS credentials and sensitive query parameters.
    // Extracts only the filename for display purposes.
    // Example: https://bucket.s3.../abc-123.png?X-Amz-Credential=... → abc-123.png
    //
    public static string MaskUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        // Split on '?' to remove query parameters containing credentials
        var baseUrl = url.Split('?')[0];
        
        // Extract filename from path
        var fileName = Path.GetFileName(baseUrl);
        
        return fileName;
    }
}
