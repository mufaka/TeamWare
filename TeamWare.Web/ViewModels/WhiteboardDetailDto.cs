namespace TeamWare.Web.ViewModels;

public class WhiteboardDetailDto : WhiteboardDto
{
    public string? CanvasData { get; set; }

    public List<WhiteboardInvitationDto> Invitations { get; set; } = new();

    public List<ActiveUserDto> ActiveUsers { get; set; } = new();
}
