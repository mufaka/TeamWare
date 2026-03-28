using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace TeamWare.Agent.Pipeline;

/// <summary>
/// Production wrapper around CopilotClient.
/// </summary>
public class CopilotClientWrapper : ICopilotClientWrapper
{
    private readonly CopilotClient _client;

    public CopilotClientWrapper(CopilotClientOptions options)
    {
        _client = new CopilotClient(options);
    }

    public async Task StartAsync()
    {
        await _client.StartAsync();
    }

    public async Task<ICopilotSessionWrapper> CreateSessionAsync(SessionConfig config)
    {
        var session = await _client.CreateSessionAsync(config);
        return new CopilotSessionWrapper(session);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
