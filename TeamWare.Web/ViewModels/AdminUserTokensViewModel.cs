using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class AdminUserTokensViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public List<PersonalAccessToken> Tokens { get; set; } = new();
}
