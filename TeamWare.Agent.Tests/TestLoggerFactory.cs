using Microsoft.Extensions.Logging;

namespace TeamWare.Agent.Tests;

public class TestLoggerFactory : ILoggerFactory
{
    private readonly ILogger _defaultLogger;

    public TestLoggerFactory(ILogger defaultLogger)
    {
        _defaultLogger = defaultLogger;
    }

    public ILogger CreateLogger(string categoryName) => _defaultLogger;

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }
}
