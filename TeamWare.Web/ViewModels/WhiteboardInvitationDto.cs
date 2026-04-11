namespace TeamWare.Web.ViewModels;

public class WhiteboardInvitationDto
{
    public int Id { get; set; }

    public int WhiteboardId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string UserDisplayName { get; set; } = string.Empty;

    public string InvitedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
