using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface ILoungeService
{
    // Message operations (16.1)
    Task<ServiceResult<LoungeMessage>> SendMessage(int? projectId, string userId, string content);
    Task<ServiceResult<LoungeMessage>> EditMessage(int messageId, string userId, string content);
    Task<ServiceResult> DeleteMessage(int messageId, string userId, bool isSiteAdmin = false);
    Task<ServiceResult<List<LoungeMessage>>> GetMessages(int? projectId, DateTime? before, int count);
    Task<ServiceResult<LoungeMessage>> GetMessage(int messageId);

    // Pin operations (16.2)
    Task<ServiceResult<LoungeMessage>> PinMessage(int messageId, string userId, bool isSiteAdmin = false);
    Task<ServiceResult<LoungeMessage>> UnpinMessage(int messageId, string userId, bool isSiteAdmin = false);
    Task<ServiceResult<List<LoungeMessage>>> GetPinnedMessages(int? projectId);

    // Reaction operations (16.3)
    Task<ServiceResult<LoungeReaction>> ToggleReaction(int messageId, string userId, string reactionType);
    Task<ServiceResult<List<ReactionSummary>>> GetReactionsForMessage(int messageId, string? currentUserId = null);

    // Unread tracking (16.4)
    Task<ServiceResult> UpdateReadPosition(string userId, int? projectId, int lastReadMessageId);
    Task<ServiceResult<List<RoomUnreadCount>>> GetUnreadCounts(string userId);
    Task<ServiceResult<int?>> GetReadPosition(string userId, int? projectId);

    // Message-to-task conversion (16.5)
    Task<ServiceResult<TaskItem>> CreateTaskFromMessage(int messageId, string userId);

    // Retention (16.6)
    Task<int> CleanupExpiredMessages();
    Task<List<int>> GetExpiredMessageIds();
}

public class ReactionSummary
{
    public string ReactionType { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool CurrentUserReacted { get; set; }
}

public class RoomUnreadCount
{
    public int? ProjectId { get; set; }
    public int Count { get; set; }
}
