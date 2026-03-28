using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;

namespace TeamWare.Agent.Pipeline;

/// <summary>
/// Production factory that creates real CopilotClient instances.
/// </summary>
public class CopilotClientWrapperFactory : ICopilotClientWrapperFactory
{
    public ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger)
    {
        var clientOptions = new CopilotClientOptions
        {
            Cwd = options.WorkingDirectory,
            Logger = logger,
            AutoStart = false
        };

        return new CopilotClientWrapper(clientOptions);
    }
}
