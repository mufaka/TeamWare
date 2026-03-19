using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class WhatsNextViewModel
{
    public List<WhatsNextItemViewModel> Tasks { get; set; } = new();
}

public class WhatsNextItemViewModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public TaskItemPriority Priority { get; set; }

    public TaskItemStatus Status { get; set; }

    public DateTime? DueDate { get; set; }

    public bool IsOverdue => DueDate.HasValue && DueDate.Value.Date < DateTime.UtcNow.Date && Status != TaskItemStatus.Done;
}
