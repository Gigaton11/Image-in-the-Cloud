using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

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

    public async Task<string> UploadFileAsync(IFormFile file)
    {
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