using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class ReviewViewModel
{
    public List<InboxItemViewModel> UnprocessedInboxItems { get; set; } = new();
    public List<ReviewTaskViewModel> ActiveTasks { get; set; } = new();
    public List<ReviewTaskViewModel> NextActions { get; set; } = new();
    public List<ReviewTaskViewModel> SomedayMaybeItems { get; set; } = new();
    public DateTime? LastReviewDate { get; set; }
    public int CurrentStep { get; set; } = 1;
}
