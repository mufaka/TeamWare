using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class ActivityLogService : IActivityLogService
{
    private readonly ApplicationDbContext _context;

    public ActivityLogService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogChange(int taskItemId, int projectId, string userId, ActivityChangeType changeType,
        string? oldValue = null, string? newValue = null)
    {
        var entry = new ActivityLogEntry
        {
            TaskItemId = taskItemId,
            ProjectId = projectId,
            UserId = userId,
            ChangeType = changeType,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = DateTime.UtcNow
        };

        _context.ActivityLogEntries.Add(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<List<ActivityLogEntry>> GetActivityForProject(int projectId, int count = 20)
    {
        return await _context.ActivityLogEntries
            .Where(a => a.ProjectId == projectId)
            .Include(a => a.User)
            .Include(a => a.TaskItem)
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<ActivityLogEntry>> GetActivityForTask(int taskItemId)
    {
        return await _context.ActivityLogEntries
            .Where(a => a.TaskItemId == taskItemId)
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }
}
