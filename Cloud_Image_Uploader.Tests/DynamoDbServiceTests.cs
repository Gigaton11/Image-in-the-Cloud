using Amazon.DynamoDBv2;
using Cloud_Image_Uploader.Models;
using Cloud_Image_Uploader.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cloud_Image_Uploader.Tests;

public class DynamoDbServiceConstructorTests
{
    [Fact]
    public void Constructor_Initializes_WithValidClient()
    {
        // Arrange
        var mockDynamoDb = new Mock<IAmazonDynamoDB>();
        var mockLogger = new Mock<ILogger<DynamoDbService>>();

        // Act
        var service = new DynamoDbService(mockDynamoDb.Object, mockLogger.Object);

        // Assert - Constructor should not throw
        Assert.NotNull(service);
        
        // Verify logger was called during initialization
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("initialized")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

public class FileMetadataTests
{
    [Fact]
    public void FileMetadata_CreateWithValidData()
    {
        // Arrange
        var fileId = "abc-123.png";
        var fileName = "my-image.png";
        var fileSize = 204800; // 200 KB
        var contentType = "image/png";
        var uploadTime = DateTime.UtcNow;
        var uploadedBy = "anonymous";

        // Act
        var metadata = new FileMetadata
        {
            FileId = fileId,
            FileName = fileName,
            FileSize = fileSize,
            ContentType = contentType,
            UploadTime = uploadTime,
            UploadedBy = uploadedBy
        };

        // Assert
        Assert.Equal(fileId, metadata.FileId);
        Assert.Equal(fileName, metadata.FileName);
        Assert.Equal(fileSize, metadata.FileSize);
        Assert.Equal(contentType, metadata.ContentType);
        Assert.Equal(uploadTime, metadata.UploadTime);
        Assert.Equal(uploadedBy, metadata.UploadedBy);
    }

    [Fact]
    public void FileMetadata_UploadTimeCanBeComparedForExpiration()
    {
        // Arrange
        var uploadTime = DateTime.UtcNow.AddMinutes(-5); // 5 minutes ago
        var expirationMinutes = 10;

        // Act
        var isExpired = DateTime.UtcNow > uploadTime.AddMinutes(expirationMinutes);

        // Assert
        Assert.False(isExpired, "File uploaded 5 minutes ago should not be expired after 10 minutes");
    }

    [Fact]
    public void FileMetadata_UploadTimeExpires_AfterExpirationWindow()
    {
        // Arrange
        var uploadTime = DateTime.UtcNow.AddMinutes(-11); // 11 minutes ago
        var expirationMinutes = 10;

        // Act
        var isExpired = DateTime.UtcNow > uploadTime.AddMinutes(expirationMinutes);

        // Assert
        Assert.True(isExpired, "File uploaded 11 minutes ago should be expired after 10 minutes");
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void FileMetadata_ContentType_SupportedImageTypes(string contentType)
    {
        // Arrange & Act
        var metadata = new FileMetadata
        {
            FileId = "test.ext",
            FileName = "test",
            FileSize = 1024,
            ContentType = contentType,
            UploadTime = DateTime.UtcNow,
            UploadedBy = "test"
        };

        // Assert
        Assert.NotNull(metadata.ContentType);
        Assert.StartsWith("image/", metadata.ContentType);
    }
}

public class DownloadRecordTests
{
    [Fact]
    public void DownloadRecord_CreateWithValidData()
    {
        // Arrange
        var fileId = "file-123.png";
        var downloadTime = DateTime.UtcNow;
        var downloadedBy = "user123";

        // Act
        var record = new DownloadRecord
        {
            FileId = fileId,
            DownloadTime = downloadTime,
            DownloadedBy = downloadedBy
        };

        // Assert
        Assert.Equal(fileId, record.FileId);
        Assert.Equal(downloadTime, record.DownloadTime);
        Assert.Equal(downloadedBy, record.DownloadedBy);
    }

    [Fact]
    public void DownloadRecord_CanTrackAnonymousDownloads()
    {
        // Act
        var record = new DownloadRecord
        {
            FileId = "test.png",
            DownloadTime = DateTime.UtcNow,
            DownloadedBy = "anonymous"
        };

        // Assert
        Assert.Equal("anonymous", record.DownloadedBy);
    }
}
