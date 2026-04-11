namespace TeamWare.Web.ViewModels;

public class WhiteboardDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    public string OwnerDisplayName { get; set; } = string.Empty;

    public int? ProjectId { get; set; }

    public string? ProjectName { get; set; }

    public string? CurrentPresenterId { get; set; }

    public string? CurrentPresenterDisplayName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsTemporary => !ProjectId.HasValue;
}
