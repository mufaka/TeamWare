using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;

namespace TeamWare.Agent.Pipeline;

/// <summary>
/// Factory for creating ICopilotClientWrapper instances.
/// Enables testing by allowing mock implementations.
/// </summary>
public interface ICopilotClientWrapperFactory
{
    ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger);
}
