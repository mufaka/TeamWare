using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class SomedayMaybeViewModel
{
    public List<SomedayMaybeItemViewModel> Tasks { get; set; } = new();
}

public class SomedayMaybeItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public TaskItemPriority Priority { get; set; }
    public DateTime UpdatedAt { get; set; }
}
