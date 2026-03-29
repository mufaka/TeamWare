using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IProjectInvitationService
{
    Task<ServiceResult<ProjectInvitation>> SendInvitation(int projectId, string invitedUserId, ProjectRole role, string invitedByUserId);

    Task<ServiceResult<List<ProjectInvitation>>> SendBulkInvitations(int projectId, List<string> invitedUserIds, ProjectRole role, string invitedByUserId);

    Task<ServiceResult<ProjectMember>> AcceptInvitation(int invitationId, string userId);

    Task<ServiceResult> DeclineInvitation(int invitationId, string userId);

    Task<ServiceResult<List<ProjectInvitation>>> GetPendingInvitationsForProject(int projectId, string requestingUserId);

    Task<ServiceResult<List<ProjectInvitation>>> GetPendingInvitationsForUser(string userId);

    Task<ServiceResult> CancelInvitation(int invitationId, string cancelledByUserId);

    Task<ServiceResult<ProjectInvitation>> ResendInvitation(int invitationId, string resentByUserId);
}
