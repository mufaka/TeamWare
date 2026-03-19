using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IActivityLogService
{
    Task LogChange(int taskItemId, int projectId, string userId, ActivityChangeType changeType,
        string? oldValue = null, string? newValue = null);

    Task<List<ActivityLogEntry>> GetActivityForProject(int projectId, int count = 20);

    Task<List<ActivityLogEntry>> GetActivityForTask(int taskItemId);
}
