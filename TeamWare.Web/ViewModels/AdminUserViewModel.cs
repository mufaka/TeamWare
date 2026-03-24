namespace TeamWare.Web.ViewModels;

public class AdminUserViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsLockedOut { get; set; }
    public bool IsAdmin { get; set; }
    public int ActivePatCount { get; set; }
}
