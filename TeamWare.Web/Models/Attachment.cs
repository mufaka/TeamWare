using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class Attachment
{
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public AttachmentEntityType EntityType { get; set; }

    public int EntityId { get; set; }

    [Required]
    public string UploadedByUserId { get; set; } = string.Empty;

    public ApplicationUser UploadedByUser { get; set; } = null!;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
