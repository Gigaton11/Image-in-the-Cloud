using System.ComponentModel.DataAnnotations;

namespace Cloud_Image_Uploader.Models;

public class ForgotPasswordViewModel
{
    [Required]
    [Display(Name = "Username or email")]
    public string Identifier { get; set; } = string.Empty;
}