using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class Whiteboard
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    public ApplicationUser Owner { get; set; } = null!;

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public string? CurrentPresenterId { get; set; }

    public ApplicationUser? CurrentPresenter { get; set; }

    public string? CanvasData { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WhiteboardInvitation> Invitations { get; set; } = new List<WhiteboardInvitation>();

    public ICollection<WhiteboardChatMessage> ChatMessages { get; set; } = new List<WhiteboardChatMessage>();
}
