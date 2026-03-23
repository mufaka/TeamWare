using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class GlobalConfiguration
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string Value { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? UpdatedByUserId { get; set; }

    public ApplicationUser? UpdatedByUser { get; set; }
}
