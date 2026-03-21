using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class ProjectInvitation
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    [Required]
    public string InvitedUserId { get; set; } = string.Empty;

    public ApplicationUser InvitedUser { get; set; } = null!;

    [Required]
    public string InvitedByUserId { get; set; } = string.Empty;

    public ApplicationUser InvitedByUser { get; set; } = null!;

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public ProjectRole Role { get; set; } = ProjectRole.Member;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RespondedAt { get; set; }
}
