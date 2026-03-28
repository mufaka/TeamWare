using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Tests;

public class FakeMcpClientFactory : ITeamWareMcpClientFactory
{
    public FakeMcpClient? LastCreatedClient { get; private set; }
    public bool ThrowOnCreate { get; set; }
    public Func<AgentIdentityOptions, FakeMcpClient>? ClientFactory { get; set; }

    public Task<ITeamWareMcpClient> CreateAsync(
        AgentIdentityOptions options,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnCreate)
        {
            throw new InvalidOperationException($"Simulated MCP client creation failure for '{options.Name}'");
        }

        var client = ClientFactory?.Invoke(options) ?? new FakeMcpClient();
        LastCreatedClient = client;
        return Task.FromResult<ITeamWareMcpClient>(client);
    }
}
