using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class InviteMemberViewModel
{
    public int ProjectId { get; set; }

    [Required]
    [EmailAddress]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;
}
