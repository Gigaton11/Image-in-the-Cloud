using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace Cloud_Image_Uploader.Services;

/// <summary>
/// Service for handling AWS S3 operations including file upload, download, and deletion.
/// Manages AWS S3 bucket interactions and generates secure pre-signed URLs for temporary access.
/// </summary>
public class S3Service
{
    /// <summary>
    /// Whitelist of allowed image file extensions.
    /// Only these formats are permitted to prevent arbitrary file uploads.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    /// <summary>
    /// Maximum file size allowed: 10 MB.
    /// Prevents large file uploads that could consume excessive storage and bandwidth.
    /// </summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    /// <summary>S3 bucket name from configuration.</summary>
    private readonly string _bucketName;
    /// <summary>AWS S3 client for API interactions.</summary>
    private readonly IAmazonS3 _s3Client;
    /// <summary>Utility for managing multi-part uploads and downloads.</summary>
    private readonly TransferUtility _transferUtility;

    /// <summary>
    /// Constructor that initializes the S3 service with AWS credentials and configuration.
    /// Throws if bucket name is not configured in appsettings.json.
    /// </summary>
    public S3Service(IConfiguration config, IAmazonS3 s3Client)
    {
        _bucketName = config["AWS:BucketName"]
            ?? throw new InvalidOperationException("AWS bucket name is not configured.");
        _s3Client = s3Client;
        _transferUtility = new TransferUtility(_s3Client);
    }

    /// <summary>
    /// Uploads a file to S3 bucket after validation.
    /// Uses a GUID to generate a unique filename, preventing collisions.
    /// Returns only the file key (not the full signed URL) for security.
    /// </summary>
    /// <param name="file">The IFormFile from the HTTP request.</param>
    /// <returns>The generated file key/filename in S3.</returns>
    public async Task<string> UploadFileAsync(IFormFile file)
    {
        ValidateUpload(file);
        // Generate unique filename: [GUID].[extension]
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

    /// <summary>
    /// Retrieves a file from S3 as a stream for downloading.
    /// Called by the Download endpoint to serve files to users.
    /// </summary>
    /// <param name="key">The file key/name in S3 bucket.</param>
    /// <returns>A stream containing the file data.</returns>
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

    /// <summary>
    /// Deletes a file from the S3 bucket.
    /// Can be used to remove expired or unwanted files.
    /// </summary>
    /// <param name="key">The file key/name to delete.</param>
    public async Task DeleteFileAsync(string key)
    {
        await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        });
    }

    /// <summary>
    /// Validates a file before upload.
    /// Checks: file exists, size limit, and file type.
    /// Multiple layers of validation (controller + service) provide defense-in-depth.
    /// </summary>
    /// <param name="file">The file to validate.</param>
    /// <exception cref="ArgumentException">Thrown if validation fails.</exception>
    private static void ValidateUpload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty.");

        if (file.Length > MaxFileSizeBytes)
            throw new ArgumentException("File too large.");

        // Validate file extension is allowed (defense-in-depth)
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("File is not a valid image format.");
    }

    /// <summary>
    /// Generates a temporary, signed URL for accessing a file in S3.
    /// The URL expires in 10 minutes and includes AWS authentication credentials.
    /// This is called server-side only and returned to the browser via redirect.
    /// </summary>
    /// <param name="key">The file key in S3 to grant access to.</param>
    /// <returns>A pre-signed URL valid for 10 minutes.</returns>
    public string GetPreSignedUrl(string key)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddMinutes(10), // 10-minute expiration
        };

        return _s3Client.GetPreSignedURL(request);
    }

    /// <summary>
    /// Masks/obscures a signed URL to hide AWS credentials and sensitive query parameters.
    /// Extracts only the filename for display purposes.
    /// Example: https://bucket.s3.../abc-123.png?X-Amz-Credential=... → abc-123.png
    /// </summary>
    /// <param name="url">The full signed URL to mask.</param>
    /// <returns>Just the filename without path or query parameters.</returns>
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
