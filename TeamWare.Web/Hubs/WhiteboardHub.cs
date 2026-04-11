using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Services;

namespace TeamWare.Web.Hubs;

[Authorize]
public class WhiteboardHub : Hub
{
    private readonly IWhiteboardService _whiteboardService;
    private readonly IWhiteboardInvitationService _whiteboardInvitationService;
    private readonly IWhiteboardChatService _whiteboardChatService;
    private readonly IWhiteboardPresenceTracker _whiteboardPresenceTracker;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<WhiteboardHub> _logger;

    public WhiteboardHub(
        IWhiteboardService whiteboardService,
        IWhiteboardInvitationService whiteboardInvitationService,
        IWhiteboardChatService whiteboardChatService,
        IWhiteboardPresenceTracker whiteboardPresenceTracker,
        ApplicationDbContext dbContext,
        ILogger<WhiteboardHub> logger)
    {
        _whiteboardService = whiteboardService;
        _whiteboardInvitationService = whiteboardInvitationService;
        _whiteboardChatService = whiteboardChatService;
        _whiteboardPresenceTracker = whiteboardPresenceTracker;
        _dbContext = dbContext;
        _logger = logger;
    }

    public static string GetGroupName(int whiteboardId) => $"whiteboard-{whiteboardId}";

    public async Task JoinBoard(int whiteboardId)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("User is not authenticated.");
        }

        var isSiteAdmin = Context.User?.IsInRole(SeedData.AdminRoleName) == true;
        var accessResult = await _whiteboardService.CanAccessAsync(whiteboardId, userId, isSiteAdmin);
        if (!accessResult.Succeeded || !accessResult.Data)
        {
            _logger.LogWarning("User {UserId} attempted to join whiteboard {WhiteboardId} without authorization", userId, whiteboardId);
            throw new HubException(accessResult.Errors.FirstOrDefault() ?? "You do not have access to this whiteboard.");
        }

        var wasAlreadyActive = await _whiteboardPresenceTracker.IsUserActiveAsync(whiteboardId, userId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(whiteboardId));
        await _whiteboardPresenceTracker.AddConnectionAsync(whiteboardId, userId, Context.ConnectionId);

        if (!wasAlreadyActive)
        {
            var displayName = await GetDisplayNameAsync(userId);
            await Clients.Group(GetGroupName(whiteboardId)).SendAsync("UserJoined", userId, displayName);
        }
    }

    public async Task LeaveBoard(int whiteboardId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(whiteboardId));

        var removal = await _whiteboardPresenceTracker.RemoveConnectionAsync(Context.ConnectionId);
        if (removal.HasValue)
        {
            await Clients.Group(GetGroupName(removal.Value.WhiteboardId)).SendAsync("UserLeft", removal.Value.UserId);
        }
    }

    public async Task SendCanvasUpdate(int whiteboardId, string canvasData)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("User is not authenticated.");
        }

        var isConnected = await _whiteboardPresenceTracker.IsUserActiveAsync(whiteboardId, userId);
        if (!isConnected)
        {
            throw new HubException("You must be connected to the whiteboard to update the canvas.");
        }

        var saveResult = await _whiteboardService.SaveCanvasAsync(whiteboardId, canvasData, userId);
        if (!saveResult.Succeeded)
        {
            throw new HubException(saveResult.Errors.FirstOrDefault() ?? "Unable to save the whiteboard.");
        }

        await Clients.OthersInGroup(GetGroupName(whiteboardId)).SendAsync("CanvasUpdated", canvasData);
    }

    public async Task SendChatMessage(int whiteboardId, string content)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("User is not authenticated.");
        }

        var isConnected = await _whiteboardPresenceTracker.IsUserActiveAsync(whiteboardId, userId);
        if (!isConnected)
        {
            throw new HubException("You must be connected to the whiteboard to send chat messages.");
        }

        var result = await _whiteboardChatService.SendMessageAsync(whiteboardId, userId, content);
        if (!result.Succeeded)
        {
            throw new HubException(result.Errors.FirstOrDefault() ?? "Unable to send chat message.");
        }

        await Clients.Group(GetGroupName(whiteboardId)).SendAsync("ChatMessageReceived", result.Data);
    }

    public async Task AssignPresenter(int whiteboardId, string userId)
    {
        var callerUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(callerUserId))
        {
            throw new HubException("User is not authenticated.");
        }

        var whiteboard = await _dbContext.Whiteboards
            .FirstOrDefaultAsync(w => w.Id == whiteboardId);
        if (whiteboard == null)
        {
            throw new HubException("Whiteboard not found.");
        }

        if (whiteboard.OwnerId != callerUserId)
        {
            throw new HubException("Only the whiteboard owner can assign the presenter.");
        }

        var isActive = await _whiteboardPresenceTracker.IsUserActiveAsync(whiteboardId, userId);
        if (!isActive)
        {
            throw new HubException("The selected user must be actively viewing the whiteboard.");
        }

        whiteboard.CurrentPresenterId = userId;
        whiteboard.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var displayName = await GetDisplayNameAsync(userId);
        await Clients.Group(GetGroupName(whiteboardId)).SendAsync("PresenterChanged", userId, displayName);
    }

    public async Task ReclaimPresenter(int whiteboardId)
    {
        var callerUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(callerUserId))
        {
            throw new HubException("User is not authenticated.");
        }

        var whiteboard = await _dbContext.Whiteboards
            .FirstOrDefaultAsync(w => w.Id == whiteboardId);
        if (whiteboard == null)
        {
            throw new HubException("Whiteboard not found.");
        }

        if (whiteboard.OwnerId != callerUserId)
        {
            throw new HubException("Only the whiteboard owner can reclaim the presenter.");
        }

        whiteboard.CurrentPresenterId = callerUserId;
        whiteboard.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var displayName = await GetDisplayNameAsync(callerUserId);
        await Clients.Group(GetGroupName(whiteboardId)).SendAsync("PresenterChanged", callerUserId, displayName);
    }

    public async Task RemoveUser(int whiteboardId, string userId)
    {
        var callerUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(callerUserId))
        {
            throw new HubException("User is not authenticated.");
        }

        var whiteboard = await _dbContext.Whiteboards
            .FirstOrDefaultAsync(w => w.Id == whiteboardId);
        if (whiteboard == null)
        {
            throw new HubException("Whiteboard not found.");
        }

        if (whiteboard.OwnerId != callerUserId)
        {
            throw new HubException("Only the whiteboard owner can remove users.");
        }

        if (whiteboard.ProjectId.HasValue)
        {
            throw new HubException("Users can only be removed from temporary whiteboards.");
        }

        var revokeResult = await _whiteboardInvitationService.RevokeAsync(whiteboardId, userId);
        if (!revokeResult.Succeeded)
        {
            throw new HubException(revokeResult.Errors.FirstOrDefault() ?? "Unable to remove the user.");
        }

        var connectionIds = await _whiteboardPresenceTracker.RemoveUserConnectionsAsync(whiteboardId, userId);
        foreach (var connectionId in connectionIds)
        {
            await Groups.RemoveFromGroupAsync(connectionId, GetGroupName(whiteboardId));
        }

        if (connectionIds.Count > 0)
        {
            await Clients.Users([userId]).SendAsync("UserRemoved", whiteboardId);
            await Clients.Group(GetGroupName(whiteboardId)).SendAsync("UserLeft", userId);
        }

        if (whiteboard.CurrentPresenterId == userId)
        {
            whiteboard.CurrentPresenterId = null;
            whiteboard.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            await Clients.Group(GetGroupName(whiteboardId)).SendAsync("PresenterChanged", null, "Unassigned");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var removal = await _whiteboardPresenceTracker.RemoveConnectionAsync(Context.ConnectionId);
        if (removal.HasValue)
        {
            var whiteboard = await _dbContext.Whiteboards.FindAsync(removal.Value.WhiteboardId);
            if (whiteboard != null && whiteboard.CurrentPresenterId == removal.Value.UserId)
            {
                whiteboard.CurrentPresenterId = null;
                whiteboard.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                await Clients.Group(GetGroupName(removal.Value.WhiteboardId)).SendAsync("PresenterChanged", null, "Unassigned");
            }

            await Clients.Group(GetGroupName(removal.Value.WhiteboardId)).SendAsync("UserLeft", removal.Value.UserId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private string? GetUserId()
    {
        return Context.UserIdentifier
            ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private async Task<string> GetDisplayNameAsync(string userId)
    {
        var displayName = await _dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(displayName) ? userId : displayName;
    }
}
