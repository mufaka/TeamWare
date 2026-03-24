using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class PersonalAccessTokenListViewModel
{
    public List<PersonalAccessToken> Tokens { get; set; } = [];

    public string? NewlyCreatedToken { get; set; }

    public CreateTokenViewModel CreateForm { get; set; } = new();
}
