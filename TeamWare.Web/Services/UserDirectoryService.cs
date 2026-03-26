using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public class UserDirectoryService : IUserDirectoryService
{
    private readonly ApplicationDbContext _context;

    public UserDirectoryService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<PagedResult<UserDirectoryEntryViewModel>>> SearchUsers(
        string? searchTerm, int page = 1, int pageSize = 20, string? userTypeFilter = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _context.Users.AsQueryable();

        query = ApplyUserTypeFilter(query, userTypeFilter);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(term) ||
                (u.Email != null && u.Email.ToLower().Contains(term)));
        }

        query = query.OrderBy(u => u.DisplayName);

        var totalCount = await query.CountAsync();

        var users = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDirectoryEntryViewModel
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email ?? string.Empty,
                AvatarUrl = u.AvatarUrl,
                IsAgent = u.IsAgent
            })
            .ToListAsync();

        var result = new PagedResult<UserDirectoryEntryViewModel>(users, totalCount, page, pageSize);
        return ServiceResult<PagedResult<UserDirectoryEntryViewModel>>.Success(result);
    }

    public async Task<ServiceResult<PagedResult<UserDirectoryEntryViewModel>>> GetUsersSorted(
        string sortBy = "displayname", bool ascending = true, int page = 1, int pageSize = 20, string? userTypeFilter = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _context.Users.AsQueryable();

        query = ApplyUserTypeFilter(query, userTypeFilter);

        query = sortBy.ToLower() switch
        {
            "email" => ascending
                ? query.OrderBy(u => u.Email)
                : query.OrderByDescending(u => u.Email),
            _ => ascending
                ? query.OrderBy(u => u.DisplayName)
                : query.OrderByDescending(u => u.DisplayName)
        };

        var totalCount = await query.CountAsync();

        var users = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDirectoryEntryViewModel
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email ?? string.Empty,
                AvatarUrl = u.AvatarUrl,
                IsAgent = u.IsAgent
            })
            .ToListAsync();

        var result = new PagedResult<UserDirectoryEntryViewModel>(users, totalCount, page, pageSize);
        return ServiceResult<PagedResult<UserDirectoryEntryViewModel>>.Success(result);
    }

    public async Task<ServiceResult<UserProfileViewModel>> GetUserProfile(string userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return ServiceResult<UserProfileViewModel>.Failure("User not found.");
        }

        var memberships = await _context.ProjectMembers
            .Where(pm => pm.UserId == userId)
            .Include(pm => pm.Project)
            .OrderBy(pm => pm.Project.Name)
            .Select(pm => new UserProjectMembershipViewModel
            {
                ProjectId = pm.ProjectId,
                ProjectName = pm.Project.Name,
                Role = pm.Role.ToString(),
                JoinedAt = pm.JoinedAt
            })
            .ToListAsync();

        var taskStats = await GetTaskStatisticsInternal(userId);

        var recentActivity = await GetRecentActivityInternal(userId);

        var profile = new UserProfileViewModel
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email ?? string.Empty,
            AvatarUrl = user.AvatarUrl,
            ProjectMemberships = memberships,
            TaskStatistics = taskStats,
            RecentActivity = recentActivity,
            LastActiveAt = user.LastActiveAt
        };

        return ServiceResult<UserProfileViewModel>.Success(profile);
    }

    public async Task<ServiceResult<UserTaskStatisticsViewModel>> GetUserTaskStatistics(string userId)
    {
        var user = await _context.Users.AnyAsync(u => u.Id == userId);
        if (!user)
        {
            return ServiceResult<UserTaskStatisticsViewModel>.Failure("User not found.");
        }

        var stats = await GetTaskStatisticsInternal(userId);
        return ServiceResult<UserTaskStatisticsViewModel>.Success(stats);
    }

    public async Task<ServiceResult<List<UserRecentActivityViewModel>>> GetUserRecentActivity(string userId)
    {
        var user = await _context.Users.AnyAsync(u => u.Id == userId);
        if (!user)
        {
            return ServiceResult<List<UserRecentActivityViewModel>>.Failure("User not found.");
        }

        var activity = await GetRecentActivityInternal(userId);
        return ServiceResult<List<UserRecentActivityViewModel>>.Success(activity);
    }

    private async Task<UserTaskStatisticsViewModel> GetTaskStatisticsInternal(string userId)
    {
        var assignedTaskIds = await _context.TaskAssignments
            .Where(ta => ta.UserId == userId)
            .Select(ta => ta.TaskItemId)
            .ToListAsync();

        var tasks = await _context.TaskItems
            .Where(t => assignedTaskIds.Contains(t.Id))
            .Select(t => new { t.Status, t.DueDate })
            .ToListAsync();

        var now = DateTime.UtcNow;

        return new UserTaskStatisticsViewModel
        {
            TasksAssigned = tasks.Count,
            TasksCompleted = tasks.Count(t => t.Status == TaskItemStatus.Done),
            TasksOverdue = tasks.Count(t =>
                t.Status != TaskItemStatus.Done &&
                t.DueDate.HasValue &&
                t.DueDate.Value < now)
        };
    }

    private async Task<List<UserRecentActivityViewModel>> GetRecentActivityInternal(string userId)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var activity = await _context.ActivityLogEntries
            .Where(a => a.UserId == userId && a.CreatedAt >= cutoff)
            .Include(a => a.Project)
            .Include(a => a.TaskItem)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .ToListAsync();

        return activity.Select(a => new UserRecentActivityViewModel
        {
            Description = FormatActivityDescription(a),
            ProjectName = a.Project.Name,
            ProjectId = a.ProjectId,
            CreatedAt = a.CreatedAt
        }).ToList();
    }

    private static string FormatActivityDescription(ActivityLogEntry entry)
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

    private static IQueryable<ApplicationUser> ApplyUserTypeFilter(IQueryable<ApplicationUser> query, string? userTypeFilter)
    {
        return userTypeFilter?.ToLower() switch
        {
            "human" => query.Where(u => !u.IsAgent),
            "agent" => query.Where(u => u.IsAgent),
            _ => query
        };
    }
}
