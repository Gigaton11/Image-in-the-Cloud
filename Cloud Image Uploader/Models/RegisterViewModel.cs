using System.ComponentModel.DataAnnotations;

namespace Cloud_Image_Uploader.Models;

public class RegisterViewModel
{
    [Required]
    [StringLength(32, MinimumLength = 3)]
    [RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "Use letters, numbers, dots, underscores, or hyphens only.")]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}