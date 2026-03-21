namespace TeamWare.Web.ViewModels;

public class UserProjectMembershipViewModel
{
    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public DateTime JoinedAt { get; set; }
}
