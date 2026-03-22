using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class LoungeReadPosition
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public int LastReadMessageId { get; set; }

    public LoungeMessage LastReadMessage { get; set; } = null!;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
