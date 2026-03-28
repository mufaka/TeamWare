using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IProgressService
{
    Task<ProjectStatistics> GetProjectStatistics(int projectId);

    Task<List<TaskItem>> GetOverdueTasks(int projectId);

    Task<List<TaskItem>> GetUpcomingDeadlines(int projectId, int days = 7);
}

public class ProjectStatistics
{
    public int TotalTasks { get; set; }

    public int TaskCountToDo { get; set; }

    public int TaskCountInProgress { get; set; }

    public int TaskCountInReview { get; set; }

    public int TaskCountDone { get; set; }

    public int TaskCountBlocked { get; set; }

    public int TaskCountError { get; set; }

    public double CompletionPercentage =>
        TotalTasks > 0 ? Math.Round((double)TaskCountDone / TotalTasks * 100, 1) : 0;
}
