using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class ProjectDashboardViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; }

    public int TotalMembers { get; set; }

    public int TaskCountToDo { get; set; }

    public int TaskCountInProgress { get; set; }

    public int TaskCountInReview { get; set; }

    public int TaskCountDone { get; set; }

    public int TotalTasks => TaskCountToDo + TaskCountInProgress + TaskCountInReview + TaskCountDone;

    public List<ProjectMemberViewModel> Members { get; set; } = new();

    public ProjectRole CurrentUserRole { get; set; }

    public double CompletionPercentage =>
        TotalTasks > 0 ? Math.Round((double)TaskCountDone / TotalTasks * 100, 1) : 0;

    public int OverdueTaskCount { get; set; }

    public List<TaskDeadlineViewModel> UpcomingDeadlines { get; set; } = new();

    public List<ActivityLogEntryViewModel> RecentActivity { get; set; } = new();
}

public class ProjectMemberViewModel
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public ProjectRole Role { get; set; }

    public DateTime JoinedAt { get; set; }
}
