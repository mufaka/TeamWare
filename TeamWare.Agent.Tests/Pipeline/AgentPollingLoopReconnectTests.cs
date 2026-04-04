using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Pipeline;

public class AgentPollingLoopReconnectTests
{
    private static AgentIdentityOptions CreateOptions(string name = "test-agent", int pollingInterval = 1)
    {
        return new AgentIdentityOptions
        {
            Name = name,
            PollingIntervalSeconds = pollingInterval,
            PersonalAccessToken = "test-pat"
        };
    }

    [Fact]
    public async Task RunAsync_TransportErrors_TriggersReconnectAfterThreshold()
    {
        // Arrange: MCP client that always throws transport errors
        var mcpClient = new FakeMcpClient
        {
            ThrowOnGetProfile = true,
            ExceptionToThrow = new TaskCanceledException("Connection timed out")
        };

        var createCount = 0;
        var factory = new FakeMcpClientFactory
        {
            ClientFactory = _ =>
            {
                createCount++;
                return new FakeMcpClient(); // Reconnect succeeds with a healthy client
            }
        };

        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(options, mcpClient, factory, copilotFactory: null, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        // Wait for at least 3 failure cycles + reconnect + 1 success cycle
        await Task.Delay(5000);
        await cts.CancelAsync();
        await loopTask;

        // Factory should have been called at least once for reconnection
        Assert.True(createCount >= 1, $"Expected factory to be called at least once, got {createCount}");

        // Should log the reconnection warning
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("consecutive transport failures"));

        // Should log successful reconnection
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("MCP client reconnected successfully"));
    }

    [Fact]
    public async Task RunAsync_BusinessErrors_DoNotTriggerReconnect()
    {
        // Arrange: MCP client that throws McpToolException (business error)
        var mcpClient = new FakeMcpClient
        {
            ThrowOnGetProfile = true,
            ExceptionToThrow = new McpToolException("get_my_profile", "Agent not found")
        };

        var factory = new FakeMcpClientFactory();

        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(options, mcpClient, factory, copilotFactory: null, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        // Wait for several cycles
        await Task.Delay(4000);
        await cts.CancelAsync();
        await loopTask;

        // Factory should NOT have been called for reconnection
        Assert.Null(factory.LastCreatedClient);

        // Should NOT log reconnection warnings
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("consecutive transport failures"));
    }

    [Fact]
    public async Task RunAsync_SuccessfulCycle_ResetsFailureCounter()
    {
        // Arrange: client that fails twice, then succeeds, then fails twice again
        var callCount = 0;
        var mcpClient = new FakeMcpClient();

        // Override GetMyProfileAsync behavior via the ThrowOnGetProfile toggle
        // We'll simulate by tracking calls and using a wrapper approach
        var failingClient = new SequentialFakeMcpClient(
            failureCount: 2,
            successCount: 2,
            failureCount2: 2);

        var factory = new FakeMcpClientFactory();

        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(options, failingClient, factory, copilotFactory: null, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        // Wait for the sequence to play out (2 fail + 2 success + 2 fail = ~6 cycles)
        await Task.Delay(8000);
        await cts.CancelAsync();
        await loopTask;

        // Should NOT have triggered reconnect — counter resets after success cycles
        Assert.Null(factory.LastCreatedClient);
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("consecutive transport failures"));
    }

    [Fact]
    public async Task RunAsync_NoFactory_LogsWarningOnThreshold()
    {
        var mcpClient = new FakeMcpClient
        {
            ThrowOnGetProfile = true,
            ExceptionToThrow = new HttpRequestException("Connection refused")
        };

        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        // No factory passed — uses the constructor without factory
        var loop = new AgentPollingLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        await Task.Delay(4000);
        await cts.CancelAsync();
        await loopTask;

        // Should log warning about no factory available
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("no MCP client factory available"));
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(System.IO.IOException), true)]
    [InlineData(typeof(McpToolException), false)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(System.Text.Json.JsonException), false)]
    public void IsTransportError_ClassifiesCorrectly(Type exceptionType, bool expected)
    {
        Exception ex = exceptionType == typeof(McpToolException)
            ? new McpToolException("test_tool", "test error")
            : (Exception)Activator.CreateInstance(exceptionType, "test")!;

        Assert.Equal(expected, AgentPollingLoop.IsTransportError(ex));
    }

    [Fact]
    public void IsTransportError_WrappedException_ChecksInner()
    {
        var inner = new HttpRequestException("Connection refused");
        var outer = new InvalidOperationException("Wrapper", inner);

        Assert.True(AgentPollingLoop.IsTransportError(outer));
    }

    /// <summary>
    /// A fake MCP client that throws transport errors for a specified number of calls,
    /// then succeeds for a number of calls, then optionally fails again.
    /// </summary>
    private class SequentialFakeMcpClient : ITeamWareMcpClient
    {
        private readonly int _failureCount1;
        private readonly int _successCount;
        private readonly int _failureCount2;
        private int _callIndex;

        public SequentialFakeMcpClient(int failureCount, int successCount, int failureCount2)
        {
            _failureCount1 = failureCount;
            _successCount = successCount;
            _failureCount2 = failureCount2;
        }

        public Task<AgentProfile> GetMyProfileAsync(CancellationToken cancellationToken = default)
        {
            var index = _callIndex++;

            if (index < _failureCount1)
                throw new HttpRequestException("Connection refused (simulated)");

            if (index < _failureCount1 + _successCount)
            {
                return Task.FromResult(new AgentProfile
                {
                    UserId = "test-user",
                    IsAgent = true,
                    IsAgentActive = true
                });
            }

            if (index < _failureCount1 + _successCount + _failureCount2)
                throw new HttpRequestException("Connection refused again (simulated)");

            return Task.FromResult(new AgentProfile
            {
                UserId = "test-user",
                IsAgent = true,
                IsAgentActive = true
            });
        }

        public Task<IReadOnlyList<AgentTask>> GetMyAssignmentsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentTask>>([]);

        public Task<AgentTaskDetail> GetTaskAsync(int taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTaskDetail { Id = taskId });

        public Task UpdateTaskStatusAsync(int taskId, string status, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddCommentAsync(int taskId, string content, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PostLoungeMessageAsync(int? projectId, string content, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
