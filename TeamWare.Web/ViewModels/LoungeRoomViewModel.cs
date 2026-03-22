namespace TeamWare.Web.ViewModels;

public class LoungeRoomViewModel
{
    public int? ProjectId { get; set; }

    public string RoomName { get; set; } = string.Empty;

    public List<LoungeMessageViewModel> Messages { get; set; } = new();

    public List<LoungeMessageViewModel> PinnedMessages { get; set; } = new();

    public int? LastReadMessageId { get; set; }

    public bool CanCreateTask { get; set; }

    public List<LoungeMemberViewModel> Members { get; set; } = new();
}
