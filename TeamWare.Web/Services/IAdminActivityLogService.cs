using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IAdminActivityLogService
{
    Task LogAction(string adminUserId, string action, string? targetUserId = null, int? targetProjectId = null, string? details = null);

    Task<ServiceResult<PagedResult<AdminActivityLog>>> GetActivityLog(int page, int pageSize);
}
