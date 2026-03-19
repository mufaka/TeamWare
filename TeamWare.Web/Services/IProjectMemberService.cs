using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IProjectMemberService
{
    Task<ServiceResult<ProjectMember>> InviteMember(int projectId, string targetUserId, string invitedByUserId);

    Task<ServiceResult> RemoveMember(int projectId, string targetUserId, string removedByUserId);

    Task<ServiceResult> UpdateMemberRole(int projectId, string targetUserId, ProjectRole newRole, string updatedByUserId);

    Task<ServiceResult<List<ProjectMember>>> GetMembers(int projectId, string requestingUserId);
}
