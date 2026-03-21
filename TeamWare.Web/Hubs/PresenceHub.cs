using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TeamWare.Web.Services;

namespace TeamWare.Web.Hubs;

[Authorize]
public class PresenceHub : Hub
{
    private readonly IPresenceService _presenceService;
    private readonly ILogger<PresenceHub> _logger;

    public PresenceHub(IPresenceService presenceService, ILogger<PresenceHub> logger)
    {
        _presenceService = presenceService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;

        if (!string.IsNullOrEmpty(userId))
        {
            _presenceService.TrackUserConnection(userId, Context.ConnectionId);
            await _presenceService.UpdateLastActive(userId);

            _logger.LogDebug("User {UserId} connected with connection {ConnectionId}", userId, Context.ConnectionId);
            await Clients.Others.SendAsync("UserOnline", userId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;

        if (!string.IsNullOrEmpty(userId))
        {
            _presenceService.TrackUserDisconnection(userId, Context.ConnectionId);

            // Only broadcast offline if user has no remaining connections
            if (!_presenceService.IsUserOnline(userId))
            {
                await _presenceService.UpdateLastActive(userId);
                _logger.LogDebug("User {UserId} disconnected (all connections closed)", userId);
                await Clients.Others.SendAsync("UserOffline", userId);
            }
            else
            {
                _logger.LogDebug("User {UserId} disconnected connection {ConnectionId} but still has other connections", userId, Context.ConnectionId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
