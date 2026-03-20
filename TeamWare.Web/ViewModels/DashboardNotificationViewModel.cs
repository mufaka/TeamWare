using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class DashboardNotificationViewModel
{
    public int Id { get; set; }
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public DateTime CreatedAt { get; set; }
}
