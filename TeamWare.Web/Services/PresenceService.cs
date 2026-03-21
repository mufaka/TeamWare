using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;

namespace TeamWare.Web.Services;

public class PresenceService : IPresenceService
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _userConnections = new();
    private static readonly object _lock = new();

    private readonly ApplicationDbContext _context;

    public PresenceService(ApplicationDbContext context)
    {
        _context = context;
    }

    public void TrackUserConnection(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_userConnections.TryGetValue(userId, out var connections))
            {
                connections = new HashSet<string>();
                _userConnections[userId] = connections;
            }

            connections.Add(connectionId);
        }
    }

    public void TrackUserDisconnection(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (_userConnections.TryGetValue(userId, out var connections))
            {
                connections.Remove(connectionId);

                if (connections.Count == 0)
                {
                    _userConnections.TryRemove(userId, out _);
                }
            }
        }
    }

    public IReadOnlySet<string> GetOnlineUsers()
    {
        lock (_lock)
        {
            return new HashSet<string>(_userConnections.Keys);
        }
    }

    public bool IsUserOnline(string userId)
    {
        lock (_lock)
        {
            return _userConnections.ContainsKey(userId);
        }
    }

    public async Task UpdateLastActive(string userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user != null)
        {
            user.LastActiveAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    // For testing: reset the static state
    public static void ResetState()
    {
        lock (_lock)
        {
            _userConnections.Clear();
        }
    }
}
