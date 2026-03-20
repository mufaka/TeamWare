using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IInboxService
{
    Task<ServiceResult<InboxItem>> AddItem(string title, string? description, string userId);

    Task<ServiceResult<InboxItem>> ClarifyItem(int inboxItemId, int projectId, TaskItemPriority priority,
        bool isNextAction, bool isSomedayMaybe, string userId);

    Task<ServiceResult<TaskItem>> ConvertToTask(int inboxItemId, int projectId, TaskItemPriority priority,
        DateTime? dueDate, bool isNextAction, bool isSomedayMaybe, string userId, string? description = null);

    Task<ServiceResult> DismissItem(int inboxItemId, string userId);

    Task<ServiceResult<InboxItem>> MoveToSomedayMaybe(int inboxItemId, int projectId, string userId);

    Task<ServiceResult<List<InboxItem>>> GetUnprocessedItems(string userId);

    Task<ServiceResult<int>> GetUnprocessedCount(string userId);
}
