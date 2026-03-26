using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class CreateAgentViewModel
{
    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(256, ErrorMessage = "Display name cannot exceed 256 characters.")]
    [Display(Name = "Display Name")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Description cannot exceed 2000 characters.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }
}
