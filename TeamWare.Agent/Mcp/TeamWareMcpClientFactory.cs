using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;

namespace TeamWare.Agent.Mcp;

public class TeamWareMcpClientFactory : ITeamWareMcpClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public TeamWareMcpClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public async Task<ITeamWareMcpClient> CreateAsync(
        AgentIdentityOptions options,
        CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<TeamWareMcpClient>();
        return await TeamWareMcpClient.CreateAsync(options, logger, cancellationToken);
    }
}
