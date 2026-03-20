using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class ReviewData
{
    public List<InboxItem> UnprocessedInboxItems { get; set; } = new();

    public List<TaskItem> ActiveTasks { get; set; } = new();

    public List<TaskItem> NextActions { get; set; } = new();

    public List<TaskItem> SomedayMaybeItems { get; set; } = new();

    public DateTime? LastReviewDate { get; set; }
}
