namespace TeamWare.Web.ViewModels;

public class WhiteboardInviteFormViewModel
{
    public int WhiteboardId { get; set; }

    public string Query { get; set; } = string.Empty;

    public List<WhiteboardInviteCandidateViewModel> Candidates { get; set; } = new();

    public List<WhiteboardInvitationDto> InvitedUsers { get; set; } = new();

    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; set; }
}
