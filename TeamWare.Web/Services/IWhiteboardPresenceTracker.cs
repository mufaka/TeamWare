namespace TeamWare.Web.Services;

public interface IWhiteboardPresenceTracker
{
    Task AddConnectionAsync(int whiteboardId, string userId, string connectionId);

    Task<(int WhiteboardId, string UserId)?> RemoveConnectionAsync(string connectionId);

    Task<List<string>> GetUserConnectionIdsAsync(int whiteboardId, string userId);

    Task<List<string>> RemoveUserConnectionsAsync(int whiteboardId, string userId);

    Task<List<string>> GetActiveUsersAsync(int whiteboardId);

    Task<bool> IsUserActiveAsync(int whiteboardId, string userId);
}
