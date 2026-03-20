using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;

    public NotificationService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task CreateNotification(string userId, string message, NotificationType type, int? referenceId = null)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var notification = new Notification
        {
            UserId = userId,
            Message = message.Length > 500 ? message[..500] : message,
            Type = type,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            ReferenceId = referenceId
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Notification>> GetUnreadForUser(string userId)
    {
        return await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
    }

    public async Task<ServiceResult> MarkAsRead(int notificationId, string userId)
    {
        var notification = await _context.Notifications.FindAsync(notificationId);
        if (notification == null)
        {
            return ServiceResult.Failure("Notification not found.");
        }

        if (notification.UserId != userId)
        {
            return ServiceResult.Failure("You can only manage your own notifications.");
        }

        notification.IsRead = true;
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DismissNotification(int notificationId, string userId)
    {
        var notification = await _context.Notifications.FindAsync(notificationId);
        if (notification == null)
        {
            return ServiceResult.Failure("Notification not found.");
        }

        if (notification.UserId != userId)
        {
            return ServiceResult.Failure("You can only manage your own notifications.");
        }

        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<int> GetUnreadCount(string userId)
    {
        return await _context.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<bool> GetInboxThresholdAlert(string userId, int threshold = 10)
    {
        var unprocessedCount = await _context.InboxItems
            .CountAsync(i => i.UserId == userId && i.Status == InboxItemStatus.Unprocessed);

        return unprocessedCount >= threshold;
    }
}
