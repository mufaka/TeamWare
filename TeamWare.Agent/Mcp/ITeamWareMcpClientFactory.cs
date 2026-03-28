using TeamWare.Agent.Configuration;

namespace TeamWare.Agent.Mcp;

public interface ITeamWareMcpClientFactory
{
    Task<ITeamWareMcpClient> CreateAsync(AgentIdentityOptions options, CancellationToken cancellationToken = default);
}
