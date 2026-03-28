using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests;

public class FakeCopilotSessionWrapper : ICopilotSessionWrapper
{
    public bool DisposeCalled { get; private set; }
    public bool ThrowOnSendAndWait { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public string? LastPromptSent { get; private set; }
    public int SendAndWaitCallCount { get; private set; }

    public Task SendAndWaitAsync(string prompt, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        SendAndWaitCallCount++;
        LastPromptSent = prompt;

        if (ThrowOnSendAndWait)
        {
            throw ExceptionToThrow ?? new InvalidOperationException("LLM processing failed (simulated)");
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        return ValueTask.CompletedTask;
    }
}
