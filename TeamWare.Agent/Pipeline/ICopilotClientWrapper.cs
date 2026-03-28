using GitHub.Copilot.SDK;

namespace TeamWare.Agent.Pipeline;

/// <summary>
/// Abstraction over the GitHub Copilot SDK to enable testing.
/// Wraps CopilotClient creation and session management.
/// </summary>
public interface ICopilotClientWrapper : IAsyncDisposable
{
    Task StartAsync();
    Task<ICopilotSessionWrapper> CreateSessionAsync(SessionConfig config);
}
