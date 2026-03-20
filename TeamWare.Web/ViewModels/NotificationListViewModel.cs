namespace TeamWare.Web.ViewModels;

public class NotificationListViewModel
{
    public List<NotificationViewModel> Notifications { get; set; } = new();

    public int UnreadCount { get; set; }
}
