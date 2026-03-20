using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class EditProfileViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "Display Name")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Avatar URL")]
    public string? AvatarUrl { get; set; }
}
