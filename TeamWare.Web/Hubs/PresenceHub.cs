using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TeamWare.Web.Hubs;

[Authorize]
public class PresenceHub : Hub
{
    private readonly ILogger<PresenceHub> _logger;

    public PresenceHub(ILogger<PresenceHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;

        if (!string.IsNullOrEmpty(userId))
        {
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
            _logger.LogDebug("User {UserId} disconnected with connection {ConnectionId}", userId, Context.ConnectionId);
            await Clients.Others.SendAsync("UserOffline", userId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
