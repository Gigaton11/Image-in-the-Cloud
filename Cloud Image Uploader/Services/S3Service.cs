using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace Cloud_Image_Uploader.Services;

//
// Service for all AWS S3 operations: uploading image variants, downloading, and deleting files.
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
    // Uploads image variants to S3.
    // Stores original, web format and thumbnail with naming convention:
    // {fileId}_original.<ext>, {fileId}_web.webp and {fileId}_thumb.webp
    // Returns the base file ID for tracking in metadata.
    //
    public async Task<string> UploadProcessedImagesAsync(IFormFile originalFile, ImageProcessingService.ProcessedImageResult processedImages)
    {
        ValidateUpload(originalFile);
        
        // Generate unique base file ID (without extension, will add _web.webp or _thumb.webp)
        var fileId = Guid.NewGuid().ToString();

        var originalFileName = BuildOriginalFileKey(fileId, originalFile.FileName);
        var webFileName = $"{fileId}_web.webp";
        var thumbnailFileName = $"{fileId}_thumb.webp";

        _logger.LogInformation("Uploading image variants: FileId={FileId}, Original={Original}, OriginalFile={OriginalFile}, WebFile={WebFile}, ThumbFile={ThumbFile}",
            fileId, originalFile.FileName, originalFileName, webFileName, thumbnailFileName);

        try
        {
            // Upload original version for lossless/format-preserving downloads
            using var originalStream = originalFile.OpenReadStream();
            var originalUploadRequest = new TransferUtilityUploadRequest
            {
                InputStream = originalStream,
                Key = originalFileName,
                BucketName = _bucketName,
                ContentType = originalFile.ContentType
            };
            await _transferUtility.UploadAsync(originalUploadRequest);
            _logger.LogInformation("Original image uploaded: {FileKey}, Size={SizeKB}KB",
                originalFileName, originalFile.Length / 1024.0);

            // Upload web-optimized version
            if (processedImages.WebFormatImage.CanSeek)
            {
                processedImages.WebFormatImage.Position = 0;
            }
            var webUploadRequest = new TransferUtilityUploadRequest
            {
                InputStream = processedImages.WebFormatImage,
                Key = webFileName,
                BucketName = _bucketName,
                ContentType = "image/webp"
            };
            await _transferUtility.UploadAsync(webUploadRequest);
            _logger.LogInformation("Web-optimized image uploaded: {FileKey}, Size={SizeKB}KB", 
                webFileName, processedImages.WebFormatSizeBytes / 1024.0);

            // Upload thumbnail version
            if (processedImages.ThumbnailImage.CanSeek)
            {
                processedImages.ThumbnailImage.Position = 0;
            }
            var thumbUploadRequest = new TransferUtilityUploadRequest
            {
                InputStream = processedImages.ThumbnailImage,
                Key = thumbnailFileName,
                BucketName = _bucketName,
                ContentType = "image/webp"
            };
            await _transferUtility.UploadAsync(thumbUploadRequest);
            _logger.LogInformation("Thumbnail image uploaded: {FileKey}, Size={SizeKB}KB",
                thumbnailFileName, processedImages.ThumbnailSizeBytes / 1024.0);

            _logger.LogInformation("All processed images uploaded successfully: {FileId}", fileId);
            return fileId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processed image upload failed for: {FileId}", fileId);
            throw;
        }
    }

    //
    // Builds the S3 object key for the original uploaded file.
    // Example: {fileId}_original.png
    //
    public static string BuildOriginalFileKey(string fileId, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        return $"{fileId}_original{extension}";
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

    //
    // Deletes a file from the S3 bucket by key.
    // Also hard-deletes any object versions/delete markers when bucket versioning is enabled.
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
            _logger.LogInformation("DeleteObject completed for key: {FileKey}", key);

            // If bucket versioning is enabled, also remove historical versions/delete markers
            // so the object is fully purged from storage.
            await DeleteAllVersionsIfAnyAsync(key);
            _logger.LogInformation("File deleted successfully (including versions when accessible): {FileKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File deletion failed for: {FileKey}", key);
            throw;
        }
    }

    private async Task DeleteAllVersionsIfAnyAsync(string key)
    {
        // Pagination markers for ListVersions.
        string? keyMarker = null;
        string? versionIdMarker = null;

        try
        {
            var hasMore = false;
            do
            {
                // Enumerate all object versions for this key and hard-delete them.
                var versionsResponse = await _s3Client.ListVersionsAsync(new ListVersionsRequest
                {
                    BucketName = _bucketName,
                    Prefix = key,
                    KeyMarker = keyMarker,
                    VersionIdMarker = versionIdMarker
                });

                foreach (var version in versionsResponse.Versions.Where(v => string.Equals(v.Key, key, StringComparison.Ordinal)))
                {
                    if (string.IsNullOrEmpty(version.VersionId))
                    {
                        continue;
                    }

                    await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
                    {
                        BucketName = _bucketName,
                        Key = key,
                        VersionId = version.VersionId
                    });
                }

                keyMarker = versionsResponse.NextKeyMarker;
                versionIdMarker = versionsResponse.NextVersionIdMarker;
                hasMore = versionsResponse.IsTruncated;
            }
            while (hasMore);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "AccessDenied" || ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // Keep base delete behavior even if version purge permissions are missing.
            _logger.LogWarning(
                ex,
                "Could not list/delete object versions for {FileKey}. Grant s3:ListBucketVersions and s3:DeleteObjectVersion for full purge.",
                key);
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

}
