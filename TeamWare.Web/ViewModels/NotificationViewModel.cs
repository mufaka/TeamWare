using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class NotificationViewModel
{
    public int Id { get; set; }

    public string Message { get; set; } = string.Empty;

    public NotificationType Type { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? ReferenceId { get; set; }
}
