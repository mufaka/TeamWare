using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests;

public class FakeCopilotClientWrapperFactory : ICopilotClientWrapperFactory
{
    public bool ThrowOnCreate { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public FakeCopilotClientWrapper? LastCreatedClient { get; private set; }
    public int CreateCallCount { get; private set; }

    public ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger)
    {
        CreateCallCount++;

        if (ThrowOnCreate)
        {
            throw ExceptionToThrow ?? new InvalidOperationException("Copilot client creation failed (simulated)");
        }

        LastCreatedClient = new FakeCopilotClientWrapper();
        return LastCreatedClient;
    }
}
