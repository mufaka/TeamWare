using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class LoungeReaction
{
    public int Id { get; set; }

    public int LoungeMessageId { get; set; }

    public LoungeMessage LoungeMessage { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    [Required]
    [StringLength(50)]
    public string ReactionType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
