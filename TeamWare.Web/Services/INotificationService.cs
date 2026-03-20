using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface INotificationService
{
    Task CreateNotification(string userId, string message, NotificationType type, int? referenceId = null);

    Task<List<Notification>> GetUnreadForUser(string userId);

    Task<ServiceResult> MarkAsRead(int notificationId, string userId);

    Task<ServiceResult> DismissNotification(int notificationId, string userId);

    Task<int> GetUnreadCount(string userId);

    Task<bool> GetInboxThresholdAlert(string userId, int threshold = 10);
}
