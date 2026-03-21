using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class SendInvitationViewModel
{
    public int ProjectId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public string Role { get; set; } = "Member";
}
