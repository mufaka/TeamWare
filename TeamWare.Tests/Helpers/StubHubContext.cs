using Microsoft.AspNetCore.SignalR;

namespace TeamWare.Tests.Helpers;

/// <summary>
/// A no-op IHubContext implementation for unit tests that need to pass
/// a hub context without actually sending SignalR messages.
/// </summary>
public class StubHubContext<THub> : IHubContext<THub> where THub : Hub
{
    public IHubClients Clients { get; } = new StubHubClients();
    public IGroupManager Groups { get; } = new StubGroupManager();

    private class StubHubClients : IHubClients
    {
        private readonly IClientProxy _proxy = new StubClientProxy();

        public IClientProxy All => _proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Client(string connectionId) => _proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
        public IClientProxy Group(string groupName) => _proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
        public IClientProxy User(string userId) => _proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
    }

    private class StubClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class StubGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
