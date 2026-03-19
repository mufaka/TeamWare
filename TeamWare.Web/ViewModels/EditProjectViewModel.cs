using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class EditProjectViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(200, ErrorMessage = "The {0} must be at most {1} characters long.")]
    [Display(Name = "Project Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "The {0} must be at most {1} characters long.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }
}
