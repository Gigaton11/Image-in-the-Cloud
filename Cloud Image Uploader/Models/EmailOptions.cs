namespace Cloud_Image_Uploader.Models;

public class EmailOptions
{
    public bool EnableSesDelivery { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Cloud Image Uploader";

    public bool ShowResetLinkInDevelopment { get; set; }
}