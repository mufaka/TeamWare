using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class InboxService : IInboxService
{
    private readonly ApplicationDbContext _context;
    private readonly ITaskService _taskService;
    private readonly INotificationService _notificationService;

    public InboxService(ApplicationDbContext context, ITaskService taskService,
        INotificationService notificationService)
    {
        _context = context;
        _taskService = taskService;
        _notificationService = notificationService;
    }

    public async Task<ServiceResult<InboxItem>> AddItem(string title, string? description, string userId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return ServiceResult<InboxItem>.Failure("Inbox item title is required.");
        }

        var item = new InboxItem
        {
            Title = title.Trim(),
            Description = description?.Trim(),
            UserId = userId,
            Status = InboxItemStatus.Unprocessed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        // Check inbox threshold and notify if exceeded (NOTIF-05)
        if (await _notificationService.GetInboxThresholdAlert(userId))
        {
            await _notificationService.CreateNotification(userId,
                "Your inbox has 10 or more unprocessed items. Consider reviewing and processing them.",
                NotificationType.InboxThreshold);
        }

        return ServiceResult<InboxItem>.Success(item);
    }

    public async Task<ServiceResult<InboxItem>> ClarifyItem(int inboxItemId, int projectId, TaskItemPriority priority,
        bool isNextAction, bool isSomedayMaybe, string userId)
    {
        var item = await _context.InboxItems
            .FirstOrDefaultAsync(i => i.Id == inboxItemId && i.UserId == userId);

        if (item == null)
        {
            return ServiceResult<InboxItem>.Failure("Inbox item not found.");
        }

        if (item.Status != InboxItemStatus.Unprocessed)
        {
            return ServiceResult<InboxItem>.Failure("Inbox item has already been processed.");
        }

        if (isNextAction && isSomedayMaybe)
        {
            return ServiceResult<InboxItem>.Failure("An item cannot be both a Next Action and Someday/Maybe.");
        }

        var isProjectMember = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);

        if (!isProjectMember)
        {
            return ServiceResult<InboxItem>.Failure("You must be a project member to clarify items to this project.");
        }

        // Convert to a task in the target project
        var taskResult = await _taskService.CreateTask(projectId, item.Title, item.Description,
            priority, null, userId);

        if (!taskResult.Succeeded)
        {
            return ServiceResult<InboxItem>.Failure(taskResult.Errors);
        }

        var task = taskResult.Data!;

        // Apply GTD flags
        if (isNextAction)
        {
            await _taskService.MarkAsNextAction(task.Id, userId);
        }
        else if (isSomedayMaybe)
        {
            await _taskService.MarkAsSomedayMaybe(task.Id, userId);
        }

        item.Status = InboxItemStatus.Processed;
        item.ConvertedToTaskId = task.Id;
        item.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult<InboxItem>.Success(item);
    }

    public async Task<ServiceResult<TaskItem>> ConvertToTask(int inboxItemId, int projectId, TaskItemPriority priority,
        DateTime? dueDate, bool isNextAction, bool isSomedayMaybe, string userId, string? description = null)
    {
        var item = await _context.InboxItems
            .FirstOrDefaultAsync(i => i.Id == inboxItemId && i.UserId == userId);

        if (item == null)
        {
            return ServiceResult<TaskItem>.Failure("Inbox item not found.");
        }

        if (item.Status != InboxItemStatus.Unprocessed)
        {
            return ServiceResult<TaskItem>.Failure("Inbox item has already been processed.");
        }

        if (isNextAction && isSomedayMaybe)
        {
            return ServiceResult<TaskItem>.Failure("A task cannot be both a Next Action and Someday/Maybe.");
        }

        var taskResult = await _taskService.CreateTask(projectId, item.Title, description ?? item.Description,
            priority, dueDate, userId);

        if (!taskResult.Succeeded)
        {
            return ServiceResult<TaskItem>.Failure(taskResult.Errors);
        }

        var task = taskResult.Data!;

        if (isNextAction)
        {
            await _taskService.MarkAsNextAction(task.Id, userId);
        }
        else if (isSomedayMaybe)
        {
            await _taskService.MarkAsSomedayMaybe(task.Id, userId);
        }

        item.Status = InboxItemStatus.Processed;
        item.ConvertedToTaskId = task.Id;
        item.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Reload the task to get updated GTD flags
        var updatedTask = await _context.TaskItems.FindAsync(task.Id);
        return ServiceResult<TaskItem>.Success(updatedTask!);
    }

    public async Task<ServiceResult> DismissItem(int inboxItemId, string userId)
    {
        var item = await _context.InboxItems
            .FirstOrDefaultAsync(i => i.Id == inboxItemId && i.UserId == userId);

        if (item == null)
        {
            return ServiceResult.Failure("Inbox item not found.");
        }

        if (item.Status != InboxItemStatus.Unprocessed)
        {
            return ServiceResult.Failure("Inbox item has already been processed.");
        }

        item.Status = InboxItemStatus.Dismissed;
        item.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<InboxItem>> MoveToSomedayMaybe(int inboxItemId, int projectId, string userId)
    {
        var item = await _context.InboxItems
            .FirstOrDefaultAsync(i => i.Id == inboxItemId && i.UserId == userId);

        if (item == null)
        {
            return ServiceResult<InboxItem>.Failure("Inbox item not found.");
        }

        if (item.Status != InboxItemStatus.Unprocessed)
        {
            return ServiceResult<InboxItem>.Failure("Inbox item has already been processed.");
        }

        var isProjectMember = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);

        if (!isProjectMember)
        {
            return ServiceResult<InboxItem>.Failure("You must be a project member to move items to this project.");
        }

        var taskResult = await _taskService.CreateTask(projectId, item.Title, item.Description,
            TaskItemPriority.Low, null, userId);

        if (!taskResult.Succeeded)
        {
            return ServiceResult<InboxItem>.Failure(taskResult.Errors);
        }

        var task = taskResult.Data!;
        await _taskService.MarkAsSomedayMaybe(task.Id, userId);

        item.Status = InboxItemStatus.Processed;
        item.ConvertedToTaskId = task.Id;
        item.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult<InboxItem>.Success(item);
    }

    public async Task<ServiceResult<List<InboxItem>>> GetUnprocessedItems(string userId)
    {
        var items = await _context.InboxItems
            .Where(i => i.UserId == userId && i.Status == InboxItemStatus.Unprocessed)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return ServiceResult<List<InboxItem>>.Success(items);
    }

    public async Task<ServiceResult<int>> GetUnprocessedCount(string userId)
    {
        var count = await _context.InboxItems
            .CountAsync(i => i.UserId == userId && i.Status == InboxItemStatus.Unprocessed);

        return ServiceResult<int>.Success(count);
    }
}
