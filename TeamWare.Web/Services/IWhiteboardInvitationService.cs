namespace TeamWare.Web.Services;

public interface IWhiteboardInvitationService
{
    Task<ServiceResult> InviteAsync(int whiteboardId, string invitedUserId, string ownerUserId);

    Task<ServiceResult> RevokeAsync(int whiteboardId, string userId);

    Task<bool> HasInvitationAsync(int whiteboardId, string userId);

    Task<ServiceResult> CleanupInvalidInvitationsAsync(int whiteboardId);
}
