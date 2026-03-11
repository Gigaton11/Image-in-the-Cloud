using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Cloud_Image_Uploader.Services;

//
// Service for processing images with multiple transformations:
// - Generate thumbnails
// - Resize for web display
// - Compress with quality settings
// - Convert to WebP format
//
public class ImageProcessingService
{
    // Thumbnail max dimensions (aspect ratio preserved)
    private const int ThumbnailMaxWidth = 220;
    private const int ThumbnailMaxHeight = 220;

    // Web format max dimensions (maintains aspect ratio)
    private const int WebFormatMaxWidth = 1920;
    private const int WebFormatMaxHeight = 1080;

    // Quality settings (0-100, where 100 is best quality)
    private const int WebQuality = 80;
    private const int ThumbnailQuality = 70;

    private readonly ILogger<ImageProcessingService> _logger;

    public ImageProcessingService(ILogger<ImageProcessingService> logger)
    {
        _logger = logger;
    }

    //
    // Processed image result containing multiple versions and metadata.
    //
    public class ProcessedImageResult
    {
        public Stream WebFormatImage { get; set; } = null!;
        public Stream ThumbnailImage { get; set; } = null!;
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
        public long OriginalSizeBytes { get; set; }
        public long WebFormatSizeBytes { get; set; }
        public long ThumbnailSizeBytes { get; set; }
    }

    //
    // Processes an uploaded image file with all transformations.
    // Returns processed versions and metadata about size reductions.
    // All output images are in WebP format for efficient web delivery.
    //
    public async Task<ProcessedImageResult> ProcessImageAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty or null");

        _logger.LogInformation("Starting image processing: {FileName}, Size={FileSizeKB}KB",
            file.FileName, file.Length / 1024.0);

        using var stream = file.OpenReadStream();
        using var originalImage = await Image.LoadAsync(stream);

        var originalWidth = originalImage.Width;
        var originalHeight = originalImage.Height;
        var originalSize = file.Length;

        _logger.LogInformation("Image loaded: {Width}x{Height} pixels", originalWidth, originalHeight);

        // Generate web-optimized version
        var webFormatStream = await GenerateWebFormatAsync(originalImage);

        // Generate thumbnail version
        var thumbnailStream = await GenerateThumbnailAsync(originalImage);

        var result = new ProcessedImageResult
        {
            WebFormatImage = webFormatStream,
            ThumbnailImage = thumbnailStream,
            OriginalWidth = originalWidth,
            OriginalHeight = originalHeight,
            OriginalSizeBytes = originalSize,
            WebFormatSizeBytes = webFormatStream.Length,
            ThumbnailSizeBytes = thumbnailStream.Length
        };

        var compressionPercent = (1 - (double)(result.WebFormatSizeBytes + result.ThumbnailSizeBytes) / originalSize) * 100;
        _logger.LogInformation(
            "Image processing completed. Original: {OriginalSize}KB, Web: {WebSize}KB, Thumbnail: {ThumbSize}KB, Compression: {CompressionPercent:F1}%",
            originalSize / 1024.0,
            webFormatStream.Length / 1024.0,
            thumbnailStream.Length / 1024.0,
            compressionPercent);

        return result;
    }

    //
    // Generates a web-optimized version: resized and converted to WebP.
    // Maintains aspect ratio within max dimensions for efficient web delivery.
    //
    private async Task<Stream> GenerateWebFormatAsync(Image originalImage)
    {
        _logger.LogDebug("Generating web-optimized image");

        using var image = originalImage.Clone(x => { }); // Clone for processing
        
        // Resize while maintaining aspect ratio
        var newSize = CalculateResizedDimensions(image.Width, image.Height, WebFormatMaxWidth, WebFormatMaxHeight);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = newSize,
            Mode = ResizeMode.Max // Maintains aspect ratio
        }));

        var stream = new MemoryStream();
        var encoder = new WebpEncoder { Quality = WebQuality };
        await image.SaveAsWebpAsync(stream, encoder);
        stream.Position = 0;

        _logger.LogDebug("Web-optimized image generated: {Width}x{Height}, {SizeKB}KB",
            image.Width, image.Height, stream.Length / 1024.0);

        return stream;
    }

    //
    // Generates a thumbnail version: small dimensions and converted to WebP.
    // Used for previews and gallery displays.
    //
    private async Task<Stream> GenerateThumbnailAsync(Image originalImage)
    {
        _logger.LogDebug("Generating thumbnail");

        using var image = originalImage.Clone(x => { }); // Clone for processing

        // Resize to thumbnail max bounds while preserving aspect ratio
        var newSize = CalculateResizedDimensions(image.Width, image.Height, ThumbnailMaxWidth, ThumbnailMaxHeight);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = newSize,
            Mode = ResizeMode.Max
        }));

        var stream = new MemoryStream();
        var encoder = new WebpEncoder { Quality = ThumbnailQuality };
        await image.SaveAsWebpAsync(stream, encoder);
        stream.Position = 0;

        _logger.LogDebug("Thumbnail generated: {Width}x{Height}, {SizeKB}KB",
            image.Width, image.Height, stream.Length / 1024.0);

        return stream;
    }

    //
    // Calculates the new dimensions for resizing while maintaining aspect ratio.
    // Ensures the image fits within maxWidth and maxHeight constraints.
    //
    private static Size CalculateResizedDimensions(int currentWidth, int currentHeight, int maxWidth, int maxHeight)
    {
        if (currentWidth <= maxWidth && currentHeight <= maxHeight)
            return new Size(currentWidth, currentHeight);

        var widthRatio = (double)maxWidth / currentWidth;
        var heightRatio = (double)maxHeight / currentHeight;
        var ratio = Math.Min(widthRatio, heightRatio);

        return new Size((int)(currentWidth * ratio), (int)(currentHeight * ratio));
    }
}
