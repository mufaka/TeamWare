using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class ReviewTaskViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskItemStatus Status { get; set; }
    public TaskItemPriority Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsNextAction { get; set; }
    public bool IsSomedayMaybe { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int ProjectId { get; set; }
}
