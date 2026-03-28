using GitHub.Copilot.SDK;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests;

public class FakeCopilotClientWrapper : ICopilotClientWrapper
{
    public bool StartCalled { get; private set; }
    public bool DisposeCalled { get; private set; }
    public bool ThrowOnStart { get; set; }
    public bool ThrowOnCreateSession { get; set; }
    public FakeCopilotSessionWrapper? LastCreatedSession { get; private set; }
    public SessionConfig? LastSessionConfig { get; private set; }

    public Task StartAsync()
    {
        StartCalled = true;

        if (ThrowOnStart)
        {
            throw new InvalidOperationException("Copilot CLI start failed (simulated)");
        }

        return Task.CompletedTask;
    }

    public Task<ICopilotSessionWrapper> CreateSessionAsync(SessionConfig config)
    {
        LastSessionConfig = config;

        if (ThrowOnCreateSession)
        {
            throw new InvalidOperationException("Session creation failed (simulated)");
        }

        LastCreatedSession = new FakeCopilotSessionWrapper();
        return Task.FromResult<ICopilotSessionWrapper>(LastCreatedSession);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        return ValueTask.CompletedTask;
    }
}
