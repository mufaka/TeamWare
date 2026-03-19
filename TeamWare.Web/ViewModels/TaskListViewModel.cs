using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class TaskListViewModel
{
    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public List<TaskListItemViewModel> Tasks { get; set; } = new();

    public TaskItemStatus? StatusFilter { get; set; }

    public TaskItemPriority? PriorityFilter { get; set; }

    public string? AssigneeFilter { get; set; }

    public string? SortBy { get; set; }

    public bool SortDescending { get; set; }

    public string? SearchQuery { get; set; }

    public List<ProjectMemberViewModel> ProjectMembers { get; set; } = new();

    public bool CanDeleteTasks { get; set; }
}

public class TaskListItemViewModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; }

    public TaskItemPriority Priority { get; set; }

    public DateTime? DueDate { get; set; }

    public bool IsNextAction { get; set; }

    public bool IsSomedayMaybe { get; set; }

    public List<string> AssigneeNames { get; set; } = new();

    public bool IsOverdue => DueDate.HasValue && DueDate.Value.Date < DateTime.UtcNow.Date && Status != TaskItemStatus.Done;
}
