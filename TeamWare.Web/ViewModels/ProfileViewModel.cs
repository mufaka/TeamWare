using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class ProfileViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string ThemePreference { get; set; } = "system";
}
