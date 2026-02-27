# Unit Testing & Logging Guide

## Overview
This project now includes comprehensive logging and unit tests to improve code quality and maintainability.

## Logging Implementation

### What Was Added
Logging has been integrated into all services and the main controller:

#### Services with Logging
1. **S3Service.cs** - Logs:
   - Service initialization
   - File upload/download operations
   - File deletion operations
   - Validation warnings (invalid extensions, oversized files)
   - Errors with exception details

2. **DynamoDbService.cs** - Logs:
   - Service initialization
   - Upload tracking (file ID, size, metadata)
   - Download tracking (file ID, user)
   - Metadata retrieval operations
   - Errors with exception details

3. **HomeController.cs** - Logs:
   - Controller initialization
   - Upload request details
   - Validation failures with reasons
   - Download authorization checks
   - File expiration warnings
   - Error conditions

### How to View Logs
Logs are output to the console during development. In `appsettings.Development.json`, you can configure logging levels:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Cloud_Image_Uploader.Services": "Debug",
      "Cloud_Image_Uploader.Controllers": "Information"
    }
  }
}
```

## Unit Tests

### Test Location
Tests are located in: `Cloud_Image_Uploader.Tests/`

### Running Tests
```bash
cd Cloud_Image_Uploader.Tests
dotnet test
```

### Test Coverage

#### S3ServiceTests (24 tests)
- **S3ServiceValidationTests**: Tests file validation logic
  - Constructor initialization logging
  - Null file rejection
  - PNG/JPEG/WebP file type validation
  - Non-image type rejection (.txt, .exe, .pdf)
  - Max file size (10MB) validation
  - Small file acceptance (1KB, 1MB, 5MB)
  - Large file rejection (11MB, 50MB)

- **S3ServicePresignedUrlTests**: Tests URL masking
  - Valid S3 URL masking
  - Null URL handling
  - Query parameter removal
  - Filename extraction

#### DynamoDbServiceTests (8 tests)
- **DynamoDbServiceConstructorTests**: Tests service initialization
  - Constructor logging
  - Proper initialization

- **FileMetadataTests**: Tests file metadata model
  - Metadata creation with valid data
  - Upload time expiration checking (10-minute window)
  - Content type validation
  - Supported image types (PNG, JPEG, WebP)

- **DownloadRecordTests**: Tests download tracking model
  - Download record creation
  - Anonymous user tracking

### Test Results
```
Total: 32
Passed: 32
Failed: 0
Success Rate: 100%
```

## Why These Tests Matter

1. **Validation Testing** - Ensures file uploads are properly validated
2. **Expiration Logic** - Verifies the 10-minute link expiration works correctly
3. **Model Testing** - Ensures data structures work as expected
4. **Integration with Mocking** - Uses Moq to test logging without external dependencies

## Benefits for Your CV

✅ **Shows testing mindset** - Demonstrates you care about code quality
✅ **Real-world patterns** - Uses xUnit (industry standard) and Moq for mocking
✅ **Security awareness** - Tests validate file type and size restrictions
✅ **Professional logging** - Structured logging for debugging and monitoring
✅ **100% test success rate** - All tests passing shows attention to detail

## Next Steps (Optional)

To further improve test coverage, you could add:
1. Integration tests that test multiple services together
2. Controller action tests using mocked services
3. S3 upload/download tests with mocked IAmazonS3 client
4. Performance tests for large file uploads

## Tools Used

- **xUnit** - Test framework
- **Moq** - Mocking framework
- **.NET 10.0** - Latest framework version

---

**Last Updated**: February 27, 2026
