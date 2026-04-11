using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class WhiteboardInvitation
{
    public int Id { get; set; }

    public int WhiteboardId { get; set; }

    public Whiteboard Whiteboard { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    [Required]
    public string InvitedByUserId { get; set; } = string.Empty;

    public ApplicationUser InvitedByUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
