using System.ComponentModel.DataAnnotations;
using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class UpdateMemberRoleViewModel
{
    public int ProjectId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public ProjectRole Role { get; set; }
}
