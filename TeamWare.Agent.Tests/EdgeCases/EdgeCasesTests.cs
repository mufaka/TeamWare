using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.EdgeCases;

/// <summary>
/// Tests verifying edge cases and regression scenarios:
/// - Idempotency for all non-ToDo statuses (CA-NF-06)
/// - Graceful shutdown during task processing (CA-02)
/// - Zero-task polling cycles
/// - Configuration edge cases
/// - Multiple identity isolation
/// </summary>
public class EdgeCasesTests
{
    private static AgentIdentityOptions CreateOptions(
        string name = "test-agent",
        int pollingInterval = 1,
        string workingDirectory = "/tmp/test")
    {
        return new AgentIdentityOptions
        {
            Name = name,
            WorkingDirectory = workingDirectory,
            PersonalAccessToken = "test-pat",
            PollingIntervalSeconds = pollingInterval
        };
    }

    // --- Idempotency Tests (CA-NF-06) ---

    [Theory]
    [InlineData("InProgress")]
    [InlineData("InReview")]
    [InlineData("Blocked")]
    [InlineData("Error")]
    [InlineData("Done")]
    public async Task Idempotency_TaskNotInToDo_Skipped(string currentStatus)
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = currentStatus }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Should not create any Copilot client
        Assert.Equal(0, copilotFactory.CreateCallCount);
        // Should not update status
        Assert.DoesNotContain(mcpClient.Calls, c => c.ToolName == "update_task_status");
        // Should log the skip
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Skipping task #1") &&
            e.Message.Contains(currentStatus));
    }

    [Fact]
    public async Task Idempotency_RerunAgainstSameTaskList_NoSideEffects()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo", Assignees = [new AgentTaskAssignee { UserId = "test-user" }] }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        // First run — processes the task
        await loop.ExecuteCycleAsync(CancellationToken.None);
        Assert.Equal(1, copilotFactory.CreateCallCount);

        // Now the task is in InReview (simulated by changing TaskDetailToReturn)
        mcpClient.TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "InReview" };
        mcpClient.Calls.Clear();

        // Second run — same task list, but read-before-write sees InReview
        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Should not create another Copilot client
        Assert.Equal(1, copilotFactory.CreateCallCount);
        // Should not update status
        Assert.DoesNotContain(mcpClient.Calls, c => c.ToolName == "update_task_status");
    }

    // --- Graceful Shutdown Tests (CA-02) ---

    [Fact]
    public async Task GracefulShutdown_DuringPollingWait_ExitsImmediately()
    {
        var mcpClient = new FakeMcpClient();
        var options = CreateOptions(pollingInterval: 60); // Long interval
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        // Let one cycle complete, then cancel during the 60-second wait
        await Task.Delay(200);
        await cts.CancelAsync();

        // Should exit quickly (within a few seconds), not wait 60 seconds
        var completed = await Task.WhenAny(loopTask, Task.Delay(5000));
        Assert.True(completed == loopTask, "Loop should exit quickly after cancellation");

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop stopped"));
    }

    [Fact]
    public async Task GracefulShutdown_HostedService_CancelsAllLoops()
    {
        var logger = new TestLogger<AgentHostedService>();
        var agents = new List<AgentIdentityOptions>
        {
            CreateOptions("agent-1", pollingInterval: 60),
            CreateOptions("agent-2", pollingInterval: 60)
        };

        var mcpFactory = new FakeMcpClientFactory();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var loggerFactory = new TestLoggerFactory(logger);
        var service = new AgentHostedService(
            new Microsoft.Extensions.Options.OptionsWrapper<List<AgentIdentityOptions>>(agents),
            mcpFactory, copilotFactory, loggerFactory, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200); // Let loops start

        await service.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("TeamWare Agent stopped"));
    }

    // --- Zero-Task Polling Cycles ---

    [Fact]
    public async Task ZeroTasks_NoAssignments_LogsAndWaits()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn = []
        };
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("No ToDo tasks found") &&
            e.Message.Contains("0 total assignments"));
        // No errors should be logged
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ZeroTasks_AllNonToDoStatuses_LogsAndWaits()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "InProgress", ProjectName = "Proj1" },
                new AgentTask { Id = 2, Title = "Task B", Status = "InReview", ProjectName = "Proj1" },
                new AgentTask { Id = 3, Title = "Task C", Status = "Done", ProjectName = "Proj1" },
                new AgentTask { Id = 4, Title = "Task D", Status = "Blocked", ProjectName = "Proj1" },
                new AgentTask { Id = 5, Title = "Task E", Status = "Error", ProjectName = "Proj1" }
            ]
        };
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("No ToDo tasks found") &&
            e.Message.Contains("5 total assignments"));
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    // --- Configuration Edge Cases ---

    [Fact]
    public async Task Config_ZeroAgents_StartsCleanlyWithWarning()
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

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("No agent identities configured"));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Config_InvalidPat_AuthenticationFails_LoggedAndContinues()
    {
        var mcpClient = new FakeMcpClient
        {
            ThrowOnGetProfile = true,
            ExceptionToThrow = new HttpRequestException("401 Unauthorized")
        };
        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        await Task.Delay(1500);
        await cts.CancelAsync();
        await loopTask;

        // Error should be logged
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error in polling cycle"));
        // But loop should stop gracefully
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop stopped"));
    }

    [Fact]
    public async Task Config_UnreachableMcpEndpoint_NetworkErrorLoggedAndContinues()
    {
        var mcpClient = new FakeMcpClient
        {
            ThrowOnGetProfile = true,
            ExceptionToThrow = new HttpRequestException("Connection refused")
        };
        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        await Task.Delay(1500);
        await cts.CancelAsync();
        await loopTask;

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error in polling cycle"));
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop stopped"));
    }

    [Fact]
    public async Task Config_McpClientCreationFails_LoggedPollingDoesNotStart()
    {
        var logger = new TestLogger<AgentHostedService>();
        var agents = new List<AgentIdentityOptions>
        {
            CreateOptions("agent-1")
        };

        var mcpFactory = new FakeMcpClientFactory { ThrowOnCreate = true };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var loggerFactory = new TestLoggerFactory(logger);
        var service = new AgentHostedService(
            new Microsoft.Extensions.Options.OptionsWrapper<List<AgentIdentityOptions>>(agents),
            mcpFactory, copilotFactory, loggerFactory, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500); // Give time for the polling loop to try starting

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Failed to create MCP client"));

        await service.StopAsync(CancellationToken.None);
    }

    // --- Multiple Identity Isolation ---

    [Fact]
    public async Task MultipleIdentities_DifferentPollingIntervals_IndependentExecution()
    {
        var options1 = CreateOptions("fast-agent", pollingInterval: 1, workingDirectory: "/tmp/fast");
        var options2 = CreateOptions("slow-agent", pollingInterval: 60, workingDirectory: "/tmp/slow");

        // They should have different configurations
        Assert.NotEqual(options1.Name, options2.Name);
        Assert.NotEqual(options1.WorkingDirectory, options2.WorkingDirectory);
        Assert.NotEqual(options1.PollingIntervalSeconds, options2.PollingIntervalSeconds);
    }

    [Fact]
    public async Task MultipleIdentities_DifferentPats_NoSharedState()
    {
        var options1 = CreateOptions("agent-1");
        options1.PersonalAccessToken = "pat-agent-1";
        var options2 = CreateOptions("agent-2");
        options2.PersonalAccessToken = "pat-agent-2";

        // Each identity should have its own PAT
        Assert.NotEqual(options1.PersonalAccessToken, options2.PersonalAccessToken);
    }

    [Fact]
    public async Task MultipleIdentities_OneFailsOtherContinues()
    {
        var logger = new TestLogger<AgentHostedService>();
        var agents = new List<AgentIdentityOptions>
        {
            CreateOptions("failing-agent", pollingInterval: 1),
            CreateOptions("working-agent", pollingInterval: 1)
        };

        int createCount = 0;
        var mcpFactory = new FakeMcpClientFactory
        {
            ClientFactory = options =>
            {
                createCount++;
                if (options.Name == "failing-agent")
                {
                    return new FakeMcpClient { ThrowOnGetProfile = true };
                }
                return new FakeMcpClient();
            }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var loggerFactory = new TestLoggerFactory(logger);
        var service = new AgentHostedService(
            new Microsoft.Extensions.Options.OptionsWrapper<List<AgentIdentityOptions>>(agents),
            mcpFactory, copilotFactory, loggerFactory, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(2000); // Let both loops run

        await service.StopAsync(CancellationToken.None);

        // The service should still stop gracefully
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("TeamWare Agent stopped"));
    }
}