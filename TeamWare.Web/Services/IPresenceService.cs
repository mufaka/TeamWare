namespace TeamWare.Web.Services;

public interface IPresenceService
{
    void TrackUserConnection(string userId, string connectionId);

    void TrackUserDisconnection(string userId, string connectionId);

    IReadOnlySet<string> GetOnlineUsers();

    bool IsUserOnline(string userId);

    Task UpdateLastActive(string userId);
}
