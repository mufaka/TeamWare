using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class ProgressService : IProgressService
{
    private readonly ApplicationDbContext _context;

    public ProgressService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProjectStatistics> GetProjectStatistics(int projectId)
    {
        var tasks = await _context.TaskItems
            .Where(t => t.ProjectId == projectId)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        return new ProjectStatistics
        {
            TaskCountToDo = tasks.FirstOrDefault(t => t.Status == TaskItemStatus.ToDo)?.Count ?? 0,
            TaskCountInProgress = tasks.FirstOrDefault(t => t.Status == TaskItemStatus.InProgress)?.Count ?? 0,
            TaskCountInReview = tasks.FirstOrDefault(t => t.Status == TaskItemStatus.InReview)?.Count ?? 0,
            TaskCountDone = tasks.FirstOrDefault(t => t.Status == TaskItemStatus.Done)?.Count ?? 0,
            TaskCountBlocked = tasks.FirstOrDefault(t => t.Status == TaskItemStatus.Blocked)?.Count ?? 0,
            TaskCountError = tasks.FirstOrDefault(t => t.Status == TaskItemStatus.Error)?.Count ?? 0,
            TotalTasks = tasks.Sum(t => t.Count)
        };
    }

    public async Task<List<TaskItem>> GetOverdueTasks(int projectId)
    {
        var today = DateTime.UtcNow.Date;

        return await _context.TaskItems
            .Where(t => t.ProjectId == projectId
                && t.DueDate.HasValue
                && t.DueDate.Value.Date < today
                && t.Status != TaskItemStatus.Done)
            .Include(t => t.Assignments)
                .ThenInclude(a => a.User)
            .OrderBy(t => t.DueDate)
            .ToListAsync();
    }

    public async Task<List<TaskItem>> GetUpcomingDeadlines(int projectId, int days = 7)
    {
        var today = DateTime.UtcNow.Date;
        var cutoff = today.AddDays(days);

        return await _context.TaskItems
            .Where(t => t.ProjectId == projectId
                && t.DueDate.HasValue
                && t.DueDate.Value.Date >= today
                && t.DueDate.Value.Date <= cutoff
                && t.Status != TaskItemStatus.Done)
            .Include(t => t.Assignments)
                .ThenInclude(a => a.User)
            .OrderBy(t => t.DueDate)
            .ToListAsync();
    }
}
