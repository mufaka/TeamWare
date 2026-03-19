using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface ITaskService
{
    Task<ServiceResult<TaskItem>> CreateTask(int projectId, string title, string? description,
        TaskItemPriority priority, DateTime? dueDate, string createdByUserId);

    Task<ServiceResult<TaskItem>> UpdateTask(int taskId, string title, string? description,
        TaskItemPriority priority, DateTime? dueDate, string userId);

    Task<ServiceResult> DeleteTask(int taskId, string userId);

    Task<ServiceResult<TaskItem>> ChangeStatus(int taskId, TaskItemStatus newStatus, string userId);

    Task<ServiceResult> AssignMembers(int taskId, IEnumerable<string> userIds, string assignedByUserId);

    Task<ServiceResult> UnassignMembers(int taskId, IEnumerable<string> userIds, string unassignedByUserId);

    Task<ServiceResult<TaskItem>> MarkAsNextAction(int taskId, string userId);

    Task<ServiceResult<TaskItem>> ClearNextAction(int taskId, string userId);

    Task<ServiceResult<TaskItem>> MarkAsSomedayMaybe(int taskId, string userId);

    Task<ServiceResult<TaskItem>> ClearSomedayMaybe(int taskId, string userId);

    Task<ServiceResult<List<TaskItem>>> GetTasksForProject(int projectId, string userId,
        TaskItemStatus? statusFilter = null, TaskItemPriority? priorityFilter = null,
        string? assigneeId = null, string? sortBy = null, bool sortDescending = false);

    Task<ServiceResult<List<TaskItem>>> SearchTasks(int projectId, string query, string userId);

    Task<ServiceResult<TaskItem>> GetTask(int taskId, string userId);

    Task<ServiceResult<List<TaskItem>>> GetWhatsNext(string userId, int limit = 10);
}
