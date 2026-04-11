using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class WhiteboardChatMessage
{
    public int Id { get; set; }

    public int WhiteboardId { get; set; }

    public Whiteboard Whiteboard { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    [Required]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
