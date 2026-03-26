namespace TeamWare.Web.ViewModels;

public class DirectoryListViewModel
{
    public List<UserDirectoryEntryViewModel> Users { get; set; } = new();

    public string? SearchTerm { get; set; }

    public string SortBy { get; set; } = "displayname";

    public bool Ascending { get; set; } = true;

    public int Page { get; set; } = 1;

    public int TotalPages { get; set; }

    public int TotalCount { get; set; }

    public string UserTypeFilter { get; set; } = "all";
}
