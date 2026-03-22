using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Web.Hubs;

[Authorize]
public class LoungeHub : Hub
{
    private readonly ILoungeService _loungeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LoungeHub> _logger;

    public LoungeHub(
        ILoungeService loungeService,
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ILogger<LoungeHub> logger)
    {
        _loungeService = loungeService;
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
    }

    // LOUNGE-58: Group naming convention
    private static string GetGroupName(int? projectId)
    {
        return projectId.HasValue
            ? $"lounge-project-{projectId.Value}"
            : "lounge-general";
    }

    /// <summary>
    /// Gets the SignalR group name for a room. Used by controllers via IHubContext
    /// to broadcast pin/unpin and task creation events.
    /// </summary>
    public static string GetRoomGroupName(int? projectId) => GetGroupName(projectId);

    // LOUNGE-61, LOUNGE-62, LOUNGE-63: Authorization checks
    private async Task<bool> CanAccessRoom(int? projectId, string userId)
    {
        if (projectId == null)
        {
            // #general — any authenticated user (already guaranteed by [Authorize])
            return true;
        }

        // Project room — must be a project member
        return await _dbContext.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
    }

    // LOUNGE-66, LOUNGE-68: Site admin check for #general operations
    private async Task<bool> IsSiteAdmin(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        return await _userManager.IsInRoleAsync(user, SeedData.AdminRoleName);
    }

    // --- Client-to-server methods (LOUNGE-59) ---

    public async Task JoinRoom(int? projectId)
    {
        var userId = Context.UserIdentifier!;

        if (!await CanAccessRoom(projectId, userId))
        {
            _logger.LogWarning("User {UserId} attempted to join room {ProjectId} without authorization",
                userId, projectId);
            throw new HubException("You do not have access to this room.");
        }

        var groupName = GetGroupName(projectId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug("User {UserId} joined room {GroupName}", userId, groupName);
    }

    public async Task LeaveRoom(int? projectId)
    {
        var groupName = GetGroupName(projectId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug("User {UserId} left room {GroupName}", Context.UserIdentifier, groupName);
    }

    // LOUNGE-09: Real-time message sending
    public async Task SendMessage(int? projectId, string content)
    {
        var userId = Context.UserIdentifier!;

        var result = await _loungeService.SendMessage(projectId, userId, content);
        if (!result.Succeeded)
        {
            throw new HubException(result.Errors.First());
        }

        var message = result.Data!;
        var groupName = GetGroupName(projectId);

        await Clients.Group(groupName).SendAsync("ReceiveMessage", new
        {
            message.Id,
            message.ProjectId,
            message.Content,
            message.CreatedAt,
            Author = new
            {
                message.User.Id,
                message.User.DisplayName,
                message.User.AvatarUrl
            }
        });
    }

    // LOUNGE-14, LOUNGE-64: Edit message (author-only, enforced by service)
    public async Task EditMessage(int messageId, string content)
    {
        var userId = Context.UserIdentifier!;

        var result = await _loungeService.EditMessage(messageId, userId, content);
        if (!result.Succeeded)
        {
            throw new HubException(result.Errors.First());
        }

        var message = result.Data!;
        var groupName = GetGroupName(message.ProjectId);

        await Clients.Group(groupName).SendAsync("MessageEdited", new
        {
            message.Id,
            message.Content,
            message.IsEdited,
            message.EditedAt
        });
    }

    // LOUNGE-19, LOUNGE-65, LOUNGE-66: Delete message with authorization
    public async Task DeleteMessage(int messageId)
    {
        var userId = Context.UserIdentifier!;
        var isSiteAdmin = await IsSiteAdmin(userId);

        // Get the message's projectId for broadcasting before deletion
        var getMessage = await _loungeService.GetMessage(messageId);
        if (!getMessage.Succeeded)
        {
            throw new HubException(getMessage.Errors.First());
        }

        var projectId = getMessage.Data!.ProjectId;

        var result = await _loungeService.DeleteMessage(messageId, userId, isSiteAdmin);
        if (!result.Succeeded)
        {
            throw new HubException(result.Errors.First());
        }

        var groupName = GetGroupName(projectId);

        await Clients.Group(groupName).SendAsync("MessageDeleted", new
        {
            Id = messageId
        });
    }

    // LOUNGE-36, LOUNGE-69, LOUNGE-70: Toggle reaction with room membership enforcement
    public async Task ToggleReaction(int messageId, string reactionType)
    {
        var userId = Context.UserIdentifier!;

        // Get the message first to determine the room for broadcasting
        var getMessage = await _loungeService.GetMessage(messageId);
        if (!getMessage.Succeeded)
        {
            throw new HubException(getMessage.Errors.First());
        }

        var result = await _loungeService.ToggleReaction(messageId, userId, reactionType);
        if (!result.Succeeded)
        {
            throw new HubException(result.Errors.First());
        }

        // Get updated reaction summaries for the message
        var reactionsResult = await _loungeService.GetReactionsForMessage(messageId, userId);
        if (!reactionsResult.Succeeded)
        {
            throw new HubException(reactionsResult.Errors.First());
        }

        var groupName = GetGroupName(getMessage.Data!.ProjectId);

        await Clients.Group(groupName).SendAsync("ReactionUpdated", new
        {
            MessageId = messageId,
            Reactions = reactionsResult.Data!.Select(r => new
            {
                r.ReactionType,
                r.Count
            })
        });
    }

    // LOUNGE-40: Mark messages as read
    public async Task MarkAsRead(int? projectId, int lastReadMessageId)
    {
        var userId = Context.UserIdentifier!;

        var result = await _loungeService.UpdateReadPosition(userId, projectId, lastReadMessageId);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Failed to update read position for user {UserId} in room {ProjectId}: {Error}",
                userId, projectId, result.Errors.FirstOrDefault());
        }
    }
}
