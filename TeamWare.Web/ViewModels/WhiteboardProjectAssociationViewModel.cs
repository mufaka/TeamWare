namespace TeamWare.Web.ViewModels;

public class WhiteboardProjectAssociationViewModel
{
    public int WhiteboardId { get; set; }

    public int? ProjectId { get; set; }

    public string? ProjectName { get; set; }

    public List<ProjectOptionViewModel> AvailableProjects { get; set; } = new();

    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; set; }
}
