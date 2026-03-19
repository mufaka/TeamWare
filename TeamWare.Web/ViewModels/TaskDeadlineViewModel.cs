using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class TaskDeadlineViewModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime DueDate { get; set; }

    public TaskItemStatus Status { get; set; }

    public TaskItemPriority Priority { get; set; }

    public List<string> AssigneeNames { get; set; } = new();

    public bool IsOverdue => DueDate.Date < DateTime.UtcNow.Date;

    public int DaysUntilDue => (DueDate.Date - DateTime.UtcNow.Date).Days;
}
