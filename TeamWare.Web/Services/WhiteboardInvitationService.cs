using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class WhiteboardInvitationService : IWhiteboardInvitationService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;

    public WhiteboardInvitationService(ApplicationDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<ServiceResult> InviteAsync(int whiteboardId, string invitedUserId, string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(invitedUserId))
        {
            return ServiceResult.Failure("Invited user ID is required.");
        }

        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return ServiceResult.Failure("Owner user ID is required.");
        }

        var whiteboard = await _context.Whiteboards
            .Include(w => w.Owner)
            .FirstOrDefaultAsync(w => w.Id == whiteboardId);

        if (whiteboard == null)
        {
            return ServiceResult.Failure("Whiteboard not found.");
        }

        if (whiteboard.OwnerId != ownerUserId)
        {
            return ServiceResult.Failure("Only the whiteboard owner can invite users.");
        }

        if (invitedUserId == whiteboard.OwnerId)
        {
            return ServiceResult.Failure("The whiteboard owner already has access.");
        }

        var invitedUser = await _context.Users.FindAsync(invitedUserId);
        if (invitedUser == null)
        {
            return ServiceResult.Failure("Invited user not found.");
        }

        var alreadyInvited = await _context.WhiteboardInvitations
            .AnyAsync(i => i.WhiteboardId == whiteboardId && i.UserId == invitedUserId);

        if (alreadyInvited)
        {
            return ServiceResult.Failure("User is already invited to this whiteboard.");
        }

        var invitation = new WhiteboardInvitation
        {
            WhiteboardId = whiteboardId,
            UserId = invitedUserId,
            InvitedByUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.WhiteboardInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        await _notificationService.CreateNotification(
            invitedUserId,
            $"{whiteboard.Owner.DisplayName} invited you to whiteboard \"{whiteboard.Title}\"",
            NotificationType.WhiteboardInvitation,
            whiteboard.Id);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RevokeAsync(int whiteboardId, string userId)
    {
        var invitation = await _context.WhiteboardInvitations
            .FirstOrDefaultAsync(i => i.WhiteboardId == whiteboardId && i.UserId == userId);

        if (invitation == null)
        {
            return ServiceResult.Failure("Invitation not found.");
        }

        _context.WhiteboardInvitations.Remove(invitation);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<bool> HasInvitationAsync(int whiteboardId, string userId)
    {
        return await _context.WhiteboardInvitations
            .AnyAsync(i => i.WhiteboardId == whiteboardId && i.UserId == userId);
    }

    public async Task<ServiceResult> CleanupInvalidInvitationsAsync(int whiteboardId)
    {
        var whiteboard = await _context.Whiteboards
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == whiteboardId);

        if (whiteboard == null)
        {
            return ServiceResult.Failure("Whiteboard not found.");
        }

        if (!whiteboard.ProjectId.HasValue)
        {
            return ServiceResult.Success();
        }

        var memberUserIds = await _context.ProjectMembers
            .Where(pm => pm.ProjectId == whiteboard.ProjectId)
            .Select(pm => pm.UserId)
            .ToListAsync();

        var invitationsToRemove = await _context.WhiteboardInvitations
            .Where(i => i.WhiteboardId == whiteboardId && !memberUserIds.Contains(i.UserId))
            .ToListAsync();

        if (invitationsToRemove.Count == 0)
        {
            return ServiceResult.Success();
        }

        _context.WhiteboardInvitations.RemoveRange(invitationsToRemove);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }
}
