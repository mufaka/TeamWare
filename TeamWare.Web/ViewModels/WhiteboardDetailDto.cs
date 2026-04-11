namespace TeamWare.Web.ViewModels;

public class WhiteboardDetailDto : WhiteboardDto
{
    public string? CanvasData { get; set; }

    public List<WhiteboardInvitationDto> Invitations { get; set; } = new();

    public List<string> ActiveUsers { get; set; } = new();
}
