using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class ProjectDashboard
{
    public Project Project { get; set; } = null!;

    public int TotalMembers { get; set; }

    public int TaskCountToDo { get; set; }

    public int TaskCountInProgress { get; set; }

    public int TaskCountInReview { get; set; }

    public int TaskCountDone { get; set; }

    public int TotalTasks => TaskCountToDo + TaskCountInProgress + TaskCountInReview + TaskCountDone;
}
