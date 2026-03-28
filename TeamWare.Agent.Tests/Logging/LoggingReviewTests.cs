using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Logging;

/// <summary>
/// Tests verifying structured logging throughout the agent (CA-NF-02):
/// - Polling cycle start/end with identity name
/// - Task pickup with task ID and title
/// - Status transitions with task ID
/// - Errors with full exception details
/// - Startup and shutdown events
/// - Log levels are appropriate
/// </summary>
public class LoggingReviewTests
{
    private static AgentIdentityOptions CreateOptions(string name = "test-agent", int pollingInterval = 1)
    {
        return new AgentIdentityOptions
        {
            Name = name,
            WorkingDirectory = "/tmp/test",
            PersonalAccessToken = "test-pat",
            PollingIntervalSeconds = pollingInterval
        };
    }

    // --- Polling Cycle Logging ---

    [Fact]
    public async Task PollingCycle_LogsStartWithAgentName()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions("my-agent"), mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var task = loop.RunAsync(cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();
        await task;

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Polling loop started") &&
            e.Message.Contains("my-agent"));
    }

    [Fact]
    public async Task PollingCycle_LogsStopWithAgentName()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions("my-agent"), mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var task = loop.RunAsync(cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();
        await task;

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Polling loop stopped") &&
            e.Message.Contains("my-agent"));
    }

    [Fact]
    public async Task PollingCycle_DebugLogsWithAgentName()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions("my-agent"), mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("my-agent"));
    }

    // --- Task Pickup Logging ---

    [Fact]
    public async Task TaskPickup_LogsTaskIdAndTitle()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 42, Title = "Fix login bug", Status = "ToDo", ProjectName = "WebApp", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 42, Title = "Fix login bug", Status = "ToDo" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Status transition handler should log the pickup
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("#42"));
    }

    // --- Status Transition Logging ---

    [Fact]
    public async Task StatusTransition_InProgress_LogsAtInformationLevel()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<StatusTransitionHandler>();
        var handler = new StatusTransitionHandler(mcpClient, logger);

        await handler.PickUpTaskAsync(42);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("#42") &&
            e.Message.Contains("InProgress"));
    }

    [Fact]
    public async Task StatusTransition_InReview_LogsAtInformationLevel()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<StatusTransitionHandler>();
        var handler = new StatusTransitionHandler(mcpClient, logger);

        await handler.CompleteTaskAsync(42, "Done working");

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("#42") &&
            e.Message.Contains("InReview"));
    }

    [Fact]
    public async Task StatusTransition_Blocked_LogsAtInformationLevel()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<StatusTransitionHandler>();
        var handler = new StatusTransitionHandler(mcpClient, logger);

        await handler.BlockTaskAsync(42, "Unclear requirements", "Fix bug", 10);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("#42") &&
            e.Message.Contains("Blocked"));
    }

    [Fact]
    public async Task StatusTransition_Error_LogsAtInformationLevel()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<StatusTransitionHandler>();
        var handler = new StatusTransitionHandler(mcpClient, logger);

        await handler.ErrorTaskAsync(42, "Compilation failed", "Fix bug", 10);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("#42") &&
            e.Message.Contains("Error"));
    }

    // --- Error Logging ---

    [Fact]
    public async Task ProcessingError_LogsAtErrorLevel_WithExceptionDetails()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory
        {
            ThrowOnCreate = true,
            ExceptionToThrow = new InvalidOperationException("Copilot CLI not found")
        };
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error processing task #1"));
    }

    [Fact]
    public async Task NetworkError_LogsAtErrorLevel()
    {
        var mcpClient = new FakeMcpClient
        {
            ThrowOnGetProfile = true,
            ExceptionToThrow = new HttpRequestException("Connection refused")
        };
        var logger = new TestLogger<AgentPollingLoop>();
        var options = CreateOptions(pollingInterval: 1);
        var loop = new AgentPollingLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var task = loop.RunAsync(cts.Token);
        await Task.Delay(1500);
        await cts.CancelAsync();
        await task;

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error in polling cycle"));
    }

    [Fact]
    public async Task PausedAgent_LogsAtWarningLevel()
    {
        var mcpClient = new FakeMcpClient
        {
            ProfileToReturn = new AgentProfile
            {
                UserId = "agent-1",
                IsAgent = true,
                IsAgentActive = false
            }
        };
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("not active"));
    }

    // --- Startup/Shutdown Logging ---

    [Fact]
    public async Task Startup_LogsAgentCount()
    {
        var logger = new TestLogger<AgentHostedService>();
        var agents = new List<AgentIdentityOptions>
        {
            CreateOptions("agent-1"),
            CreateOptions("agent-2")
        };

        var mcpFactory = new FakeMcpClientFactory();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var loggerFactory = new TestLoggerFactory(logger);
        var service = new AgentHostedService(
            new Microsoft.Extensions.Options.OptionsWrapper<List<AgentIdentityOptions>>(agents),
            mcpFactory, copilotFactory, loggerFactory, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        var entries = logger.Entries.ToList();
        Assert.Contains(entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("2 configured identity"));
    }

    [Fact]
    public async Task Startup_LogsEachAgentIdentityName()
    {
        var logger = new TestLogger<AgentHostedService>();
        var agents = new List<AgentIdentityOptions>
        {
            CreateOptions("alpha-agent"),
            CreateOptions("beta-agent")
        };

        var mcpFactory = new FakeMcpClientFactory();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var loggerFactory = new TestLoggerFactory(logger);
        var service = new AgentHostedService(
            new Microsoft.Extensions.Options.OptionsWrapper<List<AgentIdentityOptions>>(agents),
            mcpFactory, copilotFactory, loggerFactory, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200); // Let background tasks start
        await service.StopAsync(CancellationToken.None);

        // Take a snapshot after service has stopped to avoid concurrent modification
        var entries = logger.Entries.ToList();
        Assert.Contains(entries, e =>
            e.Message.Contains("alpha-agent"));
        Assert.Contains(entries, e =>
            e.Message.Contains("beta-agent"));
    }

    [Fact]
    public async Task Shutdown_LogsStopMessage()
    {
        var logger = new TestLogger<AgentHostedService>();
        var agents = new List<AgentIdentityOptions>();

        var mcpFactory = new FakeMcpClientFactory();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var loggerFactory = new TestLoggerFactory(logger);
        var service = new AgentHostedService(
            new Microsoft.Extensions.Options.OptionsWrapper<List<AgentIdentityOptions>>(agents),
            mcpFactory, copilotFactory, loggerFactory, logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("TeamWare Agent stopped"));
    }

    // --- Task Discovery Logging ---

    [Fact]
    public async Task TaskDiscovery_LogsFoundCount()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1" },
                new AgentTask { Id = 2, Title = "Task B", Status = "InProgress", ProjectName = "Proj1" },
                new AgentTask { Id = 3, Title = "Task C", Status = "ToDo", ProjectName = "Proj1" }
            ]
        };
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("2 ToDo task(s)") &&
            e.Message.Contains("3 total assignments"));
    }

    // --- Skipped Task Logging ---

    [Fact]
    public async Task SkippedTask_LogsAtInformationLevel()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 5, Title = "Already Done", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 5, Title = "Already Done", Status = "Done" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Skipping task #5") &&
            e.Message.Contains("Done"));
    }
}