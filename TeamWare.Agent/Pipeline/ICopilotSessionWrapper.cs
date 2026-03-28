namespace TeamWare.Agent.Pipeline;

/// <summary>
/// Abstraction over CopilotSession to enable testing.
/// </summary>
public interface ICopilotSessionWrapper : IAsyncDisposable
{
    Task SendAndWaitAsync(string prompt, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
