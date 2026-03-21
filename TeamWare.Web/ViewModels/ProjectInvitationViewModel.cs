using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class ProjectInvitationViewModel
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string InvitedUserId { get; set; } = string.Empty;

    public string InvitedUserDisplayName { get; set; } = string.Empty;

    public string InvitedUserEmail { get; set; } = string.Empty;

    public string InvitedByUserDisplayName { get; set; } = string.Empty;

    public ProjectRole Role { get; set; }

    public InvitationStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RespondedAt { get; set; }
}
