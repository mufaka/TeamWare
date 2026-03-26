using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class TaskDetailViewModel
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; }

    public TaskItemPriority Priority { get; set; }

    public DateTime? DueDate { get; set; }

    public bool IsNextAction { get; set; }

    public bool IsSomedayMaybe { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<TaskAssigneeViewModel> Assignees { get; set; } = new();

    public List<ProjectMemberViewModel> ProjectMembers { get; set; } = new();

    public bool CanDelete { get; set; }

    public bool IsOverdue => DueDate.HasValue && DueDate.Value.Date < DateTime.UtcNow.Date && Status != TaskItemStatus.Done;

    public List<ActivityLogEntryViewModel> ActivityHistory { get; set; } = new();

    public List<CommentViewModel> Comments { get; set; } = new();

    public string CurrentUserId { get; set; } = string.Empty;
}

public class TaskAssigneeViewModel
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsAgent { get; set; }
}
