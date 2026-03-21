namespace TeamWare.Web.ViewModels;

public class UserProfileViewModel
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public List<UserProjectMembershipViewModel> ProjectMemberships { get; set; } = new();

    public UserTaskStatisticsViewModel TaskStatistics { get; set; } = new();

    public List<UserRecentActivityViewModel> RecentActivity { get; set; } = new();
}
