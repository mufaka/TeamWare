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
    /// <summary>
    /// Creates a Copilot client using the agent's default working directory.
    /// </summary>
    ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger);

    /// <summary>
    /// Creates a Copilot client using an explicit working directory override.
    /// Used for multi-repository agents where CWD varies per task.
    /// </summary>
    ICopilotClientWrapper Create(AgentIdentityOptions options, string workingDirectory, ILogger logger);
}
