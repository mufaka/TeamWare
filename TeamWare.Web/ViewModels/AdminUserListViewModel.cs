namespace TeamWare.Web.ViewModels;

public class AdminUserListViewModel
{
    public List<AdminUserViewModel> Users { get; set; } = new();
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}
