namespace TeamWare.Web.ViewModels;

public class WhiteboardInviteCandidateViewModel
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool IsAlreadyInvited { get; set; }
}
