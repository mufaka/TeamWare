using GitHub.Copilot.SDK;

namespace TeamWare.Agent.Pipeline;

/// <summary>
/// Production wrapper around CopilotSession.
/// </summary>
public class CopilotSessionWrapper : ICopilotSessionWrapper
{
    private readonly CopilotSession _session;

    public CopilotSessionWrapper(CopilotSession session)
    {
        _session = session;
    }

    public async Task SendAndWaitAsync(string prompt, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await _session.SendAndWaitAsync(
            new MessageOptions { Prompt = prompt },
            timeout,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
    }
}
