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
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .Select(a => new
            {
                a.ChangeType,
                a.ProjectId,
                ProjectName = a.Project.Name,
                a.UserId,
                UserDisplayName = a.User != null ? a.User.DisplayName : null,
                TaskTitle = a.TaskItem != null ? a.TaskItem.Title : null,
                a.OldValue,
                a.NewValue,
                a.CreatedAt
            })
            .ToListAsync();

        var result = entries.Select(entry =>
        {
            var isMember = memberProjectIds.Contains(entry.ProjectId);

            return new GlobalActivityFeedEntryViewModel
            {
                Description = isMember
                    ? FormatFullDescription(entry.ChangeType, entry.TaskTitle, entry.OldValue, entry.NewValue)
                    : FormatMaskedDescription(entry.ChangeType),
                ProjectName = entry.ProjectName,
                ProjectId = entry.ProjectId,
                UserDisplayName = isMember
                    ? (entry.UserDisplayName ?? "Unknown")
                    : "A user",
                UserId = isMember ? entry.UserId : string.Empty,
                CreatedAt = entry.CreatedAt,
                IsMasked = !isMember
            };
        }).ToList();

        return ServiceResult<List<GlobalActivityFeedEntryViewModel>>.Success(result);
    }

    private static string FormatFullDescription(ActivityChangeType changeType, string? taskTitle, string? oldValue, string? newValue)
    {
        var title = taskTitle ?? "a task";

        return changeType switch
        {
            ActivityChangeType.Created => $"Created task \"{title}\"",
            ActivityChangeType.StatusChanged => $"Changed status of \"{title}\" from {oldValue} to {newValue}",
            ActivityChangeType.PriorityChanged => $"Changed priority of \"{title}\" from {oldValue} to {newValue}",
            ActivityChangeType.Assigned => $"Assigned to \"{title}\"",
            ActivityChangeType.Unassigned => $"Unassigned from \"{title}\"",
            ActivityChangeType.MarkedNextAction => $"Marked \"{title}\" as Next Action",
            ActivityChangeType.ClearedNextAction => $"Cleared Next Action on \"{title}\"",
            ActivityChangeType.MarkedSomedayMaybe => $"Marked \"{title}\" as Someday/Maybe",
            ActivityChangeType.ClearedSomedayMaybe => $"Cleared Someday/Maybe on \"{title}\"",
            ActivityChangeType.Updated => $"Updated \"{title}\"",
            ActivityChangeType.Deleted => $"Deleted task \"{title}\"",
            _ => $"Performed action on \"{title}\""
        };
    }

    private static string FormatMaskedDescription(ActivityChangeType changeType)
    {
        return changeType switch
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
