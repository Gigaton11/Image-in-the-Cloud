namespace Cloud_Image_Uploader.Models;

public class OwnedImageViewModel
{
    public required string FileId { get; set; }

    public required string FileName { get; set; }

    public required string ThumbnailUrl { get; set; }

    public required string DownloadUrl { get; set; }

    public required string ShareUrl { get; set; }

    public required string UploadedAtText { get; set; }

    public required string ExpiresAtText { get; set; }

    public required string ExpiresInText { get; set; }

    public required string FileSizeText { get; set; }

    public bool IsPublic { get; set; }

    public required string VisibilityLabel { get; set; }

    public required string ToggleVisibilityOption { get; set; }

    public required string ToggleVisibilityText { get; set; }
}