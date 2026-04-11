using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace TeamWare.Web.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public string ThemePreference { get; set; } = "system";

    public DateTime? LastActiveAt { get; set; }

    public bool IsAgent { get; set; }

    [StringLength(2000)]
    public string? AgentDescription { get; set; }

    public bool IsAgentActive { get; set; } = true;

    public ICollection<Whiteboard> OwnedWhiteboards { get; set; } = new List<Whiteboard>();

    public ICollection<WhiteboardInvitation> WhiteboardInvitations { get; set; } = new List<WhiteboardInvitation>();

    public ICollection<LoungeMessage> LoungeMessages { get; set; } = new List<LoungeMessage>();

    public ICollection<PersonalAccessToken> PersonalAccessTokens { get; set; } = new List<PersonalAccessToken>();

    public AgentConfiguration? AgentConfiguration { get; set; }

    public ICollection<AgentTaskAssignmentPermission> AgentTaskAssignmentPermissions { get; set; } = new List<AgentTaskAssignmentPermission>();

    public ICollection<AgentTaskAssignmentPermission> AgentAssignmentAuthorizations { get; set; } = new List<AgentTaskAssignmentPermission>();
}
