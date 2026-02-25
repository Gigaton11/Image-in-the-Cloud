namespace Cloud_Image_Uploader.Models
{
    // In FileMetadata.cs
    public class FileMetadata
    {
        public required string FileId { get; set; }
        public required string FileName { get; set; }
        public long FileSize { get; set; }
        public required string ContentType { get; set; }
        public DateTime UploadTime { get; set; }
        public string? UploadedBy { get; set; }
    }
}
   
