using System.ComponentModel.DataAnnotations;

namespace Cloud_Image_Uploader.Models;

public class LoginViewModel
{
    [Required]
    [Display(Name = "Username or email")]
    public string Identifier { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}