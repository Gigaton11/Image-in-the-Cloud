using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Cloud_Image_Uploader.Services;

public class S3Service
{
    private readonly string _bucketName;
    private readonly IAmazonS3 _s3Client;

public S3Service(IConfiguration config)
{
    _bucketName = config["AWS:BucketName"];
    _s3Client = new AmazonS3Client(
        config["AWS:AccessKey"],
        config["AWS:SecretKey"],
        Amazon.RegionEndpoint.GetBySystemName(config["AWS:Region"]));
}

    // Uploads a file to S3 and returns a pre-signed URL    
    public async Task<string> UploadFileAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty.");

        if (file.Length > 10 * 1024 * 1024) // 10 MB limit
            throw new ArgumentException("File too large.");

        if (!file.FileName.EndsWith(".jpg") && !file.FileName.EndsWith(".png") && !file.FileName.EndsWith(".webp")) //Make sure only allows jpg, png and webp
            throw new ArgumentException("File is not a valid image format.");

        var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);

        using var stream = file.OpenReadStream();
        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = stream,
            Key = fileName,
            BucketName = _bucketName,
            ContentType = file.ContentType
        };

        var fileTransferUtility = new TransferUtility(_s3Client);
        await fileTransferUtility.UploadAsync(uploadRequest);

        return GetPreSignedUrl(fileName); // Return signed URL instead of raw URL
    }

    public async Task<Stream> DownloadFileAsync(string key)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        var response = await _s3Client.GetObjectAsync(request);
        return response.ResponseStream; // Return stream for direct download
    }

    public async Task DeleteFileAsync(string key)
    {
        await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        });
    }

    private string GetPreSignedUrl(string key)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddMinutes(10), // expires after 10 minutes
        };

        return _s3Client.GetPreSignedURL(request);
    }
}