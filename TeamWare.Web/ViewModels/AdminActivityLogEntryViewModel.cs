namespace TeamWare.Web.ViewModels;

public class AdminActivityLogEntryViewModel
{
    public int Id { get; set; }
    public string AdminDisplayName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetUserDisplayName { get; set; }
    public string? TargetProjectName { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
