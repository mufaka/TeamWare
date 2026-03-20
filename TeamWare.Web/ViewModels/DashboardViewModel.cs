namespace TeamWare.Web.ViewModels;

public class DashboardViewModel
{
    public int InboxUnprocessedCount { get; set; }
    public List<WhatsNextItemViewModel> WhatsNextItems { get; set; } = new();
    public List<DashboardProjectViewModel> Projects { get; set; } = new();
    public List<DashboardDeadlineViewModel> UpcomingDeadlines { get; set; } = new();
    public DateTime? LastReviewDate { get; set; }
    public bool IsReviewDue { get; set; }
    public int UnreadNotificationCount { get; set; }
    public List<DashboardNotificationViewModel> RecentNotifications { get; set; } = new();
}
