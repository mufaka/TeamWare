namespace TeamWare.Web.ViewModels;

public class UserRecentActivityViewModel
{
    public string Description { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    public DateTime CreatedAt { get; set; }
}
