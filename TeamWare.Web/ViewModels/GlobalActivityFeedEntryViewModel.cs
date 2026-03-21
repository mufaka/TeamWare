namespace TeamWare.Web.ViewModels;

public class GlobalActivityFeedEntryViewModel
{
    public string Description { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    public string UserDisplayName { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public bool IsMasked { get; set; }
}
