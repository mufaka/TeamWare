using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public class GlobalActivityService : IGlobalActivityService
{
    private readonly ApplicationDbContext _context;

    public GlobalActivityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<List<GlobalActivityFeedEntryViewModel>>> GetGlobalActivityFeed(
        string viewerUserId, int count = 50)
    {
        if (string.IsNullOrEmpty(viewerUserId))
        {
            return ServiceResult<List<GlobalActivityFeedEntryViewModel>>.Failure("Viewer user ID is required.");
        }

        if (count < 1) count = 50;
        if (count > 200) count = 200;

        // Get the projects the viewer is a member of
        var memberProjectIds = await _context.ProjectMembers
            .Where(pm => pm.UserId == viewerUserId)
            .Select(pm => pm.ProjectId)
            .ToListAsync();

        // Get recent activity across all projects
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.CreatedAt >= cutoff)
            .Include(a => a.Project)
            .Include(a => a.TaskItem)
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .ToListAsync();

        var result = entries.Select(entry =>
        {
            var isMember = memberProjectIds.Contains(entry.ProjectId);

            return new GlobalActivityFeedEntryViewModel
            {
                Description = isMember
                    ? FormatFullDescription(entry)
                    : FormatMaskedDescription(entry),
                ProjectName = entry.Project.Name,
                ProjectId = entry.ProjectId,
                UserDisplayName = isMember
                    ? (entry.User?.DisplayName ?? "Unknown")
                    : "A user",
                UserId = isMember ? entry.UserId : string.Empty,
                CreatedAt = entry.CreatedAt,
                IsMasked = !isMember
            };
        }).ToList();

        return ServiceResult<List<GlobalActivityFeedEntryViewModel>>.Success(result);
    }

    private static string FormatFullDescription(ActivityLogEntry entry)
    {
        var taskTitle = entry.TaskItem?.Title ?? "a task";

        return entry.ChangeType switch
        {
            ActivityChangeType.Created => $"Created task \"{taskTitle}\"",
            ActivityChangeType.StatusChanged => $"Changed status of \"{taskTitle}\" from {entry.OldValue} to {entry.NewValue}",
            ActivityChangeType.PriorityChanged => $"Changed priority of \"{taskTitle}\" from {entry.OldValue} to {entry.NewValue}",
            ActivityChangeType.Assigned => $"Assigned to \"{taskTitle}\"",
            ActivityChangeType.Unassigned => $"Unassigned from \"{taskTitle}\"",
            ActivityChangeType.MarkedNextAction => $"Marked \"{taskTitle}\" as Next Action",
            ActivityChangeType.ClearedNextAction => $"Cleared Next Action on \"{taskTitle}\"",
            ActivityChangeType.MarkedSomedayMaybe => $"Marked \"{taskTitle}\" as Someday/Maybe",
            ActivityChangeType.ClearedSomedayMaybe => $"Cleared Someday/Maybe on \"{taskTitle}\"",
            ActivityChangeType.Updated => $"Updated \"{taskTitle}\"",
            ActivityChangeType.Deleted => $"Deleted task \"{taskTitle}\"",
            _ => $"Performed action on \"{taskTitle}\""
        };
    }

    private static string FormatMaskedDescription(ActivityLogEntry entry)
    {
        return entry.ChangeType switch
        {
            ActivityChangeType.Created => "A task was created",
            ActivityChangeType.StatusChanged => "A task status was changed",
            ActivityChangeType.PriorityChanged => "A task priority was changed",
            ActivityChangeType.Assigned => "A user was assigned to a task",
            ActivityChangeType.Unassigned => "A user was unassigned from a task",
            ActivityChangeType.MarkedNextAction => "A task was marked as Next Action",
            ActivityChangeType.ClearedNextAction => "Next Action was cleared on a task",
            ActivityChangeType.MarkedSomedayMaybe => "A task was marked as Someday/Maybe",
            ActivityChangeType.ClearedSomedayMaybe => "Someday/Maybe was cleared on a task",
            ActivityChangeType.Updated => "A task was updated",
            ActivityChangeType.Deleted => "A task was deleted",
            _ => "Activity occurred"
        };
    }
}
