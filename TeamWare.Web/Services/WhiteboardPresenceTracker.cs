using System.Collections.Concurrent;
using System.Linq;

namespace TeamWare.Web.Services;

public class WhiteboardPresenceTracker : IWhiteboardPresenceTracker
{
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, int>> _whiteboardUsers = new();

    public Task AddConnectionAsync(int whiteboardId, string userId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(connectionId))
        {
            return Task.CompletedTask;
        }

        _connections[connectionId] = new ConnectionEntry(whiteboardId, userId);

        var users = _whiteboardUsers.GetOrAdd(whiteboardId, _ => new ConcurrentDictionary<string, int>());
        users.AddOrUpdate(userId, 1, (_, count) => count + 1);

        return Task.CompletedTask;
    }

    public Task<(int WhiteboardId, string UserId)?> RemoveConnectionAsync(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || !_connections.TryRemove(connectionId, out var connection))
        {
            return Task.FromResult<(int WhiteboardId, string UserId)?>(null);
        }

        if (!_whiteboardUsers.TryGetValue(connection.WhiteboardId, out var users))
        {
            return Task.FromResult<(int WhiteboardId, string UserId)?>(null);
        }

        var remaining = users.AddOrUpdate(connection.UserId, 0, (_, count) => Math.Max(0, count - 1));
        if (remaining > 0)
        {
            return Task.FromResult<(int WhiteboardId, string UserId)?>(null);
        }

        users.TryRemove(connection.UserId, out _);
        if (users.IsEmpty)
        {
            _whiteboardUsers.TryRemove(connection.WhiteboardId, out _);
        }

        return Task.FromResult<(int WhiteboardId, string UserId)?>((connection.WhiteboardId, connection.UserId));
    }

    public Task<List<string>> GetUserConnectionIdsAsync(int whiteboardId, string userId)
    {
        var connectionIds = _connections
            .Where(kvp => kvp.Value.WhiteboardId == whiteboardId && kvp.Value.UserId == userId)
            .Select(kvp => kvp.Key)
            .OrderBy(id => id)
            .ToList();

        return Task.FromResult(connectionIds);
    }

    public async Task<List<string>> RemoveUserConnectionsAsync(int whiteboardId, string userId)
    {
        var connectionIds = await GetUserConnectionIdsAsync(whiteboardId, userId);
        foreach (var connectionId in connectionIds)
        {
            await RemoveConnectionAsync(connectionId);
        }

        return connectionIds;
    }

    public Task<List<string>> GetActiveUsersAsync(int whiteboardId)
    {
        if (!_whiteboardUsers.TryGetValue(whiteboardId, out var users))
        {
            return Task.FromResult(new List<string>());
        }

        return Task.FromResult(users.Keys.OrderBy(userId => userId).ToList());
    }

    public Task<bool> IsUserActiveAsync(int whiteboardId, string userId)
    {
        var isActive = _whiteboardUsers.TryGetValue(whiteboardId, out var users)
            && users.TryGetValue(userId, out var count)
            && count > 0;

        return Task.FromResult(isActive);
    }

    private sealed record ConnectionEntry(int WhiteboardId, string UserId);
}
