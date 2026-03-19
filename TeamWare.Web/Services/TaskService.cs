using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class TaskService : ITaskService
{
    private readonly ApplicationDbContext _context;
    private readonly IActivityLogService _activityLog;

    public TaskService(ApplicationDbContext context, IActivityLogService activityLog)
    {
        _context = context;
        _activityLog = activityLog;
    }

    private async Task<bool> IsProjectMember(int projectId, string userId)
    {
        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
    }

    private async Task<bool> IsOwnerOrAdmin(int projectId, string userId)
    {
        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId
                && (pm.Role == ProjectRole.Owner || pm.Role == ProjectRole.Admin));
    }

    public async Task<ServiceResult<TaskItem>> CreateTask(int projectId, string title, string? description,
        TaskItemPriority priority, DateTime? dueDate, string createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return ServiceResult<TaskItem>.Failure("Task title is required.");
        }

        if (!await IsProjectMember(projectId, createdByUserId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to create tasks.");
        }

        var task = new TaskItem
        {
            Title = title.Trim(),
            Description = description?.Trim(),
            Status = TaskItemStatus.ToDo,
            Priority = priority,
            DueDate = dueDate,
            ProjectId = projectId,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        await _activityLog.LogChange(task.Id, projectId, createdByUserId,
            Models.ActivityChangeType.Created, newValue: task.Status.ToString());

        return ServiceResult<TaskItem>.Success(task);
    }

    public async Task<ServiceResult<TaskItem>> UpdateTask(int taskId, string title, string? description,
        TaskItemPriority priority, DateTime? dueDate, string userId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return ServiceResult<TaskItem>.Failure("Task title is required.");
        }

        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult<TaskItem>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, userId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to edit tasks.");
        }

        var oldPriority = task.Priority;
        task.Title = title.Trim();
        task.Description = description?.Trim();
        task.Priority = priority;
        task.DueDate = dueDate;
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (oldPriority != priority)
        {
            await _activityLog.LogChange(taskId, task.ProjectId, userId,
                Models.ActivityChangeType.PriorityChanged, oldPriority.ToString(), priority.ToString());
        }
        else
        {
            await _activityLog.LogChange(taskId, task.ProjectId, userId,
                Models.ActivityChangeType.Updated);
        }

        return ServiceResult<TaskItem>.Success(task);
    }

    public async Task<ServiceResult> DeleteTask(int taskId, string userId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult.Failure("Task not found.");
        }

        if (!await IsOwnerOrAdmin(task.ProjectId, userId))
        {
            return ServiceResult.Failure("Only project owners and admins can delete tasks.");
        }

        var taskProjectId = task.ProjectId;
        var taskTitle = task.Title;
        _context.TaskItems.Remove(task);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<TaskItem>> ChangeStatus(int taskId, TaskItemStatus newStatus, string userId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult<TaskItem>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, userId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to change task status.");
        }

        var oldStatus = task.Status;
        task.Status = newStatus;
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _activityLog.LogChange(taskId, task.ProjectId, userId,
            Models.ActivityChangeType.StatusChanged, oldStatus.ToString(), newStatus.ToString());

        return ServiceResult<TaskItem>.Success(task);
    }

    public async Task<ServiceResult> AssignMembers(int taskId, IEnumerable<string> userIds, string assignedByUserId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, assignedByUserId))
        {
            return ServiceResult.Failure("You must be a project member to assign tasks.");
        }

        var userIdList = userIds.ToList();
        foreach (var userId in userIdList)
        {
            if (!await IsProjectMember(task.ProjectId, userId))
            {
                return ServiceResult.Failure($"User is not a member of this project.");
            }

            var existing = await _context.TaskAssignments
                .AnyAsync(ta => ta.TaskItemId == taskId && ta.UserId == userId);

            if (!existing)
            {
                _context.TaskAssignments.Add(new TaskAssignment
                {
                    TaskItemId = taskId,
                    UserId = userId,
                    AssignedAt = DateTime.UtcNow
                });
            }
        }

        task.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        foreach (var userId in userIdList)
        {
            var user = await _context.Users.FindAsync(userId);
            var displayName = user?.DisplayName ?? userId;
            await _activityLog.LogChange(taskId, task.ProjectId, assignedByUserId,
                Models.ActivityChangeType.Assigned, newValue: displayName);
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UnassignMembers(int taskId, IEnumerable<string> userIds, string unassignedByUserId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, unassignedByUserId))
        {
            return ServiceResult.Failure("You must be a project member to unassign tasks.");
        }

        var assignments = await _context.TaskAssignments
            .Where(ta => ta.TaskItemId == taskId && userIds.Contains(ta.UserId))
            .ToListAsync();

        _context.TaskAssignments.RemoveRange(assignments);
        task.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        foreach (var assignment in assignments)
        {
            var user = await _context.Users.FindAsync(assignment.UserId);
            var displayName = user?.DisplayName ?? assignment.UserId;
            await _activityLog.LogChange(taskId, task.ProjectId, unassignedByUserId,
                Models.ActivityChangeType.Unassigned, oldValue: displayName);
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<TaskItem>> MarkAsNextAction(int taskId, string userId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult<TaskItem>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, userId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to modify tasks.");
        }

        task.IsNextAction = true;
        task.IsSomedayMaybe = false;
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _activityLog.LogChange(taskId, task.ProjectId, userId,
            Models.ActivityChangeType.MarkedNextAction);

        return ServiceResult<TaskItem>.Success(task);
    }

    public async Task<ServiceResult<TaskItem>> ClearNextAction(int taskId, string userId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult<TaskItem>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, userId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to modify tasks.");
        }

        task.IsNextAction = false;
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _activityLog.LogChange(taskId, task.ProjectId, userId,
            Models.ActivityChangeType.ClearedNextAction);

        return ServiceResult<TaskItem>.Success(task);
    }

    public async Task<ServiceResult<TaskItem>> MarkAsSomedayMaybe(int taskId, string userId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult<TaskItem>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, userId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to modify tasks.");
        }

        task.IsSomedayMaybe = true;
        task.IsNextAction = false;
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _activityLog.LogChange(taskId, task.ProjectId, userId,
            Models.ActivityChangeType.MarkedSomedayMaybe);

        return ServiceResult<TaskItem>.Success(task);
    }

    public async Task<ServiceResult<TaskItem>> ClearSomedayMaybe(int taskId, string userId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null)
        {
            return ServiceResult<TaskItem>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, userId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to modify tasks.");
        }

        task.IsSomedayMaybe = false;
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _activityLog.LogChange(taskId, task.ProjectId, userId,
            Models.ActivityChangeType.ClearedSomedayMaybe);

        return ServiceResult<TaskItem>.Success(task);
    }

    public async Task<ServiceResult<List<TaskItem>>> GetTasksForProject(int projectId, string userId,
        TaskItemStatus? statusFilter = null, TaskItemPriority? priorityFilter = null,
        string? assigneeId = null, string? sortBy = null, bool sortDescending = false)
    {
        if (!await IsProjectMember(projectId, userId))
        {
            return ServiceResult<List<TaskItem>>.Failure("You must be a project member to view tasks.");
        }

        var query = _context.TaskItems
            .Where(t => t.ProjectId == projectId)
            .Include(t => t.Assignments)
                .ThenInclude(a => a.User)
            .Include(t => t.CreatedBy)
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(t => t.Status == statusFilter.Value);
        }

        if (priorityFilter.HasValue)
        {
            query = query.Where(t => t.Priority == priorityFilter.Value);
        }

        if (!string.IsNullOrEmpty(assigneeId))
        {
            query = query.Where(t => t.Assignments.Any(a => a.UserId == assigneeId));
        }

        query = sortBy?.ToLowerInvariant() switch
        {
            "priority" => sortDescending
                ? query.OrderByDescending(t => t.Priority)
                : query.OrderBy(t => t.Priority),
            "duedate" => sortDescending
                ? query.OrderByDescending(t => t.DueDate)
                : query.OrderBy(t => t.DueDate),
            "status" => sortDescending
                ? query.OrderByDescending(t => t.Status)
                : query.OrderBy(t => t.Status),
            "title" => sortDescending
                ? query.OrderByDescending(t => t.Title)
                : query.OrderBy(t => t.Title),
            _ => query.OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate).ThenByDescending(t => t.UpdatedAt)
        };

        var tasks = await query.ToListAsync();

        return ServiceResult<List<TaskItem>>.Success(tasks);
    }

    public async Task<ServiceResult<List<TaskItem>>> SearchTasks(int projectId, string searchQuery, string userId)
    {
        if (!await IsProjectMember(projectId, userId))
        {
            return ServiceResult<List<TaskItem>>.Failure("You must be a project member to search tasks.");
        }

        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return ServiceResult<List<TaskItem>>.Failure("Search query is required.");
        }

        var normalizedQuery = searchQuery.Trim().ToLowerInvariant();

        var tasks = await _context.TaskItems
            .Where(t => t.ProjectId == projectId &&
                (t.Title.ToLower().Contains(normalizedQuery) ||
                 (t.Description != null && t.Description.ToLower().Contains(normalizedQuery))))
            .Include(t => t.Assignments)
                .ThenInclude(a => a.User)
            .Include(t => t.CreatedBy)
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.UpdatedAt)
            .ToListAsync();

        return ServiceResult<List<TaskItem>>.Success(tasks);
    }

    public async Task<ServiceResult<TaskItem>> GetTask(int taskId, string userId)
    {
        var task = await _context.TaskItems
            .Include(t => t.Project)
            .Include(t => t.Assignments)
                .ThenInclude(a => a.User)
            .Include(t => t.CreatedBy)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null)
        {
            return ServiceResult<TaskItem>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, userId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to view this task.");
        }

        return ServiceResult<TaskItem>.Success(task);
    }

    public async Task<ServiceResult<List<TaskItem>>> GetWhatsNext(string userId, int limit = 10)
    {
        var userProjectIds = await _context.ProjectMembers
            .Where(pm => pm.UserId == userId)
            .Select(pm => pm.ProjectId)
            .ToListAsync();

        var tasks = await _context.TaskItems
            .Where(t => userProjectIds.Contains(t.ProjectId)
                && t.IsNextAction
                && t.Status != TaskItemStatus.Done
                && !t.IsSomedayMaybe)
            .Include(t => t.Project)
            .Include(t => t.Assignments)
                .ThenInclude(a => a.User)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.UpdatedAt)
            .Take(limit)
            .ToListAsync();

        return ServiceResult<List<TaskItem>>.Success(tasks);
    }

    public async Task<ServiceResult<List<TaskItem>>> GetSomedayMaybe(string userId)
    {
        var userProjectIds = await _context.ProjectMembers
            .Where(pm => pm.UserId == userId)
            .Select(pm => pm.ProjectId)
            .ToListAsync();

        var tasks = await _context.TaskItems
            .Where(t => userProjectIds.Contains(t.ProjectId)
                && t.IsSomedayMaybe
                && t.Status != TaskItemStatus.Done)
            .Include(t => t.Project)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync();

        return ServiceResult<List<TaskItem>>.Success(tasks);
    }
}
