using Microsoft.AspNetCore.Identity;

namespace TeamWare.Web.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public string ThemePreference { get; set; } = "system";

    public DateTime? LastActiveAt { get; set; }

    public ICollection<LoungeMessage> LoungeMessages { get; set; } = new List<LoungeMessage>();

    public ICollection<PersonalAccessToken> PersonalAccessTokens { get; set; } = new List<PersonalAccessToken>();
}
