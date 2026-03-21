namespace TeamWare.Web.ViewModels;

public class AdminActivityLogViewModel
{
    public List<AdminActivityLogEntryViewModel> Entries { get; set; } = new();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}
