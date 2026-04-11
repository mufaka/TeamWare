namespace TeamWare.Web.ViewModels;

public class WhiteboardSessionViewModel
{
    public WhiteboardDetailDto Whiteboard { get; set; } = new();

    public WhiteboardProjectAssociationViewModel ProjectAssociation { get; set; } = new();

    public List<WhiteboardChatMessageDto> ChatMessages { get; set; } = new();

    public bool IsOwner { get; set; }

    public bool IsPresenter { get; set; }

    public bool CanDraw { get; set; }

    public bool IsTemporary { get; set; }

    public bool IsSiteAdmin { get; set; }

    public List<ProjectOptionViewModel> AvailableProjects { get; set; } = new();
}
