using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class AdminActivityLog
{
    public int Id { get; set; }

    public string AdminUserId { get; set; } = string.Empty;

    public ApplicationUser AdminUser { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string Action { get; set; } = string.Empty;

    public string? TargetUserId { get; set; }

    public ApplicationUser? TargetUser { get; set; }

    public int? TargetProjectId { get; set; }

    public Project? TargetProject { get; set; }

    [StringLength(1000)]
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
