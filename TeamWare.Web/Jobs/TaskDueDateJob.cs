using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Jobs;

public class TaskDueDateJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TaskDueDateJob> _logger;

    public TaskDueDateJob(ApplicationDbContext context, ILogger<TaskDueDateJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Execute()
    {
        _logger.LogInformation("Task due-date job started.");

        var cutoffDate = DateTime.UtcNow.AddDays(2);

        var tasks = await _context.TaskItems
            .Where(t => t.DueDate.HasValue
                && t.DueDate.Value <= cutoffDate
                && t.Status == TaskItemStatus.ToDo
                && !t.IsNextAction
                && !t.IsSomedayMaybe)
            .ToListAsync();

        foreach (var task in tasks)
        {
            task.IsNextAction = true;
            task.UpdatedAt = DateTime.UtcNow;
        }

        if (tasks.Count > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Task due-date job completed. Promoted {Count} task(s) to Next Action.", tasks.Count);
    }
}
