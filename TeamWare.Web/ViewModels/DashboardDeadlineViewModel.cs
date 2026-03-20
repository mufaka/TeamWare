using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class DashboardDeadlineViewModel
{
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public TaskItemStatus Status { get; set; }
}
