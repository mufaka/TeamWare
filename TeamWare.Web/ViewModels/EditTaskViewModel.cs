using System.ComponentModel.DataAnnotations;
using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class EditTaskViewModel
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    [Required]
    [StringLength(300, ErrorMessage = "The {0} must be at most {1} characters long.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000, ErrorMessage = "The {0} must be at most {1} characters long.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Priority")]
    public TaskItemPriority Priority { get; set; }

    [Display(Name = "Due Date")]
    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }
}
