using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class PersonalAccessToken
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string TokenPrefix { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}
