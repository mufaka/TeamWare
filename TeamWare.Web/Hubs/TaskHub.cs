using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;

namespace TeamWare.Web.Hubs;

[Authorize]
public class TaskHub : Hub
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<TaskHub> _logger;

    public TaskHub(ApplicationDbContext dbContext, ILogger<TaskHub> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public static string GetGroupName(int taskId) => $"task-{taskId}";

    public async Task JoinTask(int taskId)
    {
        var userId = Context.UserIdentifier!;

        var isMember = await _dbContext.TaskItems
            .Where(t => t.Id == taskId)
            .SelectMany(t => t.Project.Members)
            .AnyAsync(pm => pm.UserId == userId);

        if (!isMember)
        {
            _logger.LogWarning("User {UserId} attempted to join task {TaskId} group without project membership",
                userId, taskId);
            throw new HubException("You do not have access to this task.");
        }

        var groupName = GetGroupName(taskId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug("User {UserId} joined task group {GroupName}", userId, groupName);
    }

    public async Task LeaveTask(int taskId)
    {
        var groupName = GetGroupName(taskId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug("User {UserId} left task group {GroupName}", Context.UserIdentifier, groupName);
    }
}
