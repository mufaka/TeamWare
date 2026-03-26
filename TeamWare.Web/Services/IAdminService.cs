using TeamWare.Web.Models;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public interface IAdminService
{
    Task<ServiceResult<PagedResult<ApplicationUser>>> GetAllUsers(string? searchTerm, int page, int pageSize);

    Task<ServiceResult> LockUser(string targetUserId, string adminUserId);

    Task<ServiceResult> UnlockUser(string targetUserId, string adminUserId);

    Task<ServiceResult> ResetPassword(string targetUserId, string newPassword, string adminUserId);

    Task<ServiceResult> PromoteToAdmin(string targetUserId, string adminUserId);

    Task<ServiceResult> DemoteToUser(string targetUserId, string adminUserId);

    Task<ServiceResult<SystemStatistics>> GetSystemStatistics();

    Task<ServiceResult<(ApplicationUser User, string RawToken)>> CreateAgentUser(string displayName, string? agentDescription, string adminUserId);

    Task<ServiceResult> UpdateAgentUser(string userId, string displayName, string? agentDescription, string adminUserId);

    Task<ServiceResult> SetAgentActive(string userId, bool isActive, string adminUserId);

    Task<ServiceResult<List<AgentUserSummary>>> GetAgentUsers();

    Task<ServiceResult> DeleteAgentUser(string userId, string adminUserId);
}
