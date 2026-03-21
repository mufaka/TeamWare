namespace TeamWare.Web.ViewModels;

public class UserDirectoryEntryViewModel
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public bool IsOnline { get; set; }
}
