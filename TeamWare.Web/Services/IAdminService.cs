using TeamWare.Web.Models;

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
}
