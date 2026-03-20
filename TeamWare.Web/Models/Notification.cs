using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class Notification
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    [Required]
    [StringLength(500)]
    public string Message { get; set; } = string.Empty;

    public NotificationType Type { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? ReferenceId { get; set; }
}
