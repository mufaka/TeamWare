using System.ComponentModel.DataAnnotations;
using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class InboxClarifyViewModel
{
    public int InboxItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Display(Name = "Description")]
    [StringLength(2000, ErrorMessage = "Description must be at most 2000 characters.")]
    public string? TaskDescription { get; set; }

    [Required]
    [Display(Name = "Project")]
    public int ProjectId { get; set; }

    [Display(Name = "Priority")]
    public TaskItemPriority Priority { get; set; } = TaskItemPriority.Medium;

    [Display(Name = "Due Date")]
    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    [Display(Name = "Mark as Next Action")]
    public bool IsNextAction { get; set; }

    [Display(Name = "Mark as Someday/Maybe")]
    public bool IsSomedayMaybe { get; set; }

    public List<ProjectOptionViewModel> AvailableProjects { get; set; } = new();
}
