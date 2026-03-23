using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class AttachmentViewModel
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string UploadedByDisplayName { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; }

    public bool CanDelete { get; set; }

    public string FormattedFileSize => FormatFileSize(FileSizeBytes);

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
    };
}

public class AttachmentListViewModel
{
    public AttachmentEntityType EntityType { get; set; }

    public int EntityId { get; set; }

    public string UploadUrl { get; set; } = string.Empty;

    public string DownloadUrlTemplate { get; set; } = string.Empty;

    public string DeleteUrlTemplate { get; set; } = string.Empty;

    public List<AttachmentViewModel> Attachments { get; set; } = [];

    public bool CanUpload { get; set; }
}
