using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class CreateTokenViewModel
{
    [Required]
    [StringLength(100, ErrorMessage = "Token name must be 100 characters or fewer.")]
    [Display(Name = "Token Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Expiration Date")]
    public DateTime? ExpiresAt { get; set; }
}
