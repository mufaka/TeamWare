using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class UserReview
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public DateTime CompletedAt { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}
