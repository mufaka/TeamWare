using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Pipeline;

public class AgentPollingLoopTests
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

    private static AgentPollingLoop CreateLoop(
        AgentIdentityOptions options,
        FakeMcpClient mcpClient,
        TestLogger<AgentPollingLoop>? logger = null)
    {
        logger ??= new TestLogger<AgentPollingLoop>();
        return new AgentPollingLoop(options, mcpClient, logger);
    }

    [Fact]
    public async Task ExecuteCycle_CallsGetMyProfile()
    {
        var mcpClient = new FakeMcpClient();
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(mcpClient.Calls, c => c.ToolName == "get_my_profile");
    }

    [Fact]
    public async Task ExecuteCycle_AgentNotActive_SkipsAssignments()
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
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(mcpClient.Calls, c => c.ToolName == "get_my_profile");
        Assert.DoesNotContain(mcpClient.Calls, c => c.ToolName == "my_assignments");
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("not active"));
    }

    [Fact]
    public async Task ExecuteCycle_AgentActive_CallsMyAssignments()
    {
        var mcpClient = new FakeMcpClient
        {
            ProfileToReturn = new AgentProfile
            {
                UserId = "agent-1",
                IsAgent = true,
                IsAgentActive = true
            }
        };
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(mcpClient.Calls, c => c.ToolName == "get_my_profile");
        Assert.Contains(mcpClient.Calls, c => c.ToolName == "my_assignments");
    }

    [Fact]
    public async Task ExecuteCycle_FiltersTodoTasksOnly()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1" },
                new AgentTask { Id = 2, Title = "Task B", Status = "InProgress", ProjectName = "Proj1" },
                new AgentTask { Id = 3, Title = "Task C", Status = "ToDo", ProjectName = "Proj1" },
                new AgentTask { Id = 4, Title = "Task D", Status = "InReview", ProjectName = "Proj1" },
                new AgentTask { Id = 5, Title = "Task E", Status = "Done", ProjectName = "Proj1" },
                new AgentTask { Id = 6, Title = "Task F", Status = "Blocked", ProjectName = "Proj1" },
                new AgentTask { Id = 7, Title = "Task G", Status = "Error", ProjectName = "Proj1" }
            ]
        };
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Should log "Would process" for ToDo tasks only (IDs 1 and 3)
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Would process task #1"));
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Would process task #3"));
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("Would process task #2"));
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("Would process task #4"));
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("Would process task #5"));
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("Would process task #6"));
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("Would process task #7"));

        // Should log the correct count
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("2 ToDo task(s)"));
    }

    [Fact]
    public async Task ExecuteCycle_NoTodoTasks_LogsAndReturns()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "InProgress", ProjectName = "Proj1" },
                new AgentTask { Id = 2, Title = "Task B", Status = "Done", ProjectName = "Proj1" }
            ]
        };
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("No ToDo tasks found") &&
            e.Message.Contains("2 total assignments"));
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("Would process"));
    }

    [Fact]
    public async Task RunAsync_HandlesCancellation()
    {
        var mcpClient = new FakeMcpClient();
        var options = CreateOptions(pollingInterval: 60);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        // Let one cycle run
        await Task.Delay(100);
        await cts.CancelAsync();

        await loopTask;

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop started"));
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop stopped"));
    }

    [Fact]
    public async Task RunAsync_WaitsPollingInterval()
    {
        var mcpClient = new FakeMcpClient();
        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        // Wait enough time for ~2 cycles (1 second interval)
        await Task.Delay(2500);
        await cts.CancelAsync();
        await loopTask;

        // Should have called get_my_profile at least twice
        var profileCalls = mcpClient.Calls.Count(c => c.ToolName == "get_my_profile");
        Assert.True(profileCalls >= 2, $"Expected at least 2 profile calls, got {profileCalls}");
    }

    // 38.2 Infrastructure Error Handling Tests

    [Fact]
    public async Task ExecuteCycle_ProfileCheckThrows_LogsErrorAndContinues()
    {
        var mcpClient = new FakeMcpClient { ThrowOnGetProfile = true };
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        // Should not throw — error is caught internally by RunAsync
        // But ExecuteCycleAsync will throw, caught by RunAsync's try/catch
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            loop.ExecuteCycleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_ProfileCheckThrows_LogsErrorAndContinuesLoop()
    {
        var mcpClient = new FakeMcpClient { ThrowOnGetProfile = true };
        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        // Wait for at least one cycle
        await Task.Delay(1500);
        await cts.CancelAsync();
        await loopTask;

        // Error should be logged
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error in polling cycle"));

        // Loop should still stop gracefully
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop stopped"));
    }

    [Fact]
    public async Task RunAsync_AssignmentCheckThrows_LogsErrorAndContinuesLoop()
    {
        var mcpClient = new FakeMcpClient { ThrowOnGetAssignments = true };
        var options = CreateOptions(pollingInterval: 1);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        await Task.Delay(1500);
        await cts.CancelAsync();
        await loopTask;

        // Should have called get_my_profile (which succeeded)
        Assert.Contains(mcpClient.Calls, c => c.ToolName == "get_my_profile");
        // Should have attempted my_assignments (which threw)
        Assert.Contains(mcpClient.Calls, c => c.ToolName == "my_assignments");

        // Error should be logged
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error in polling cycle"));

        // Loop should still stop gracefully
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop stopped"));
    }

    [Fact]
    public async Task ExecuteCycle_LogsDiscoveredTaskDetails()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 42, Title = "Fix login bug", Status = "ToDo", ProjectName = "WebApp" }
            ]
        };
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("#42") &&
            e.Message.Contains("Fix login bug") &&
            e.Message.Contains("WebApp"));
    }

    // --- Server Configuration Merge Integration Tests (SACFG-TEST-07, SACFG-TEST-08) ---

    [Fact]
    public async Task ExecuteCycle_NoServerConfig_UsesLocalConfigUnchanged()
    {
        var mcpClient = new FakeMcpClient
        {
            ProfileToReturn = new AgentProfile
            {
                UserId = "agent-1",
                IsAgent = true,
                IsAgentActive = true,
                Configuration = null
            }
        };
        var options = CreateOptions();
        options.Model = "local-model";
        options.PollingIntervalSeconds = 45;
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal("local-model", options.Model);
        Assert.Equal(45, options.PollingIntervalSeconds);
    }

    [Fact]
    public async Task ExecuteCycle_WithServerConfig_MergesCorrectly()
    {
        var mcpClient = new FakeMcpClient
        {
            ProfileToReturn = new AgentProfile
            {
                UserId = "agent-1",
                IsAgent = true,
                IsAgentActive = true,
                Configuration = new AgentProfileConfiguration
                {
                    Model = "server-model",
                    PollingIntervalSeconds = 30,
                    Repositories =
                    [
                        new AgentProfileRepository { ProjectName = "ServerProject", Url = "https://server.git" }
                    ]
                }
            }
        };
        // Default options: PollingInterval=1 (non-default), Model=null (default)
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Model was null (default) → server value applied
        Assert.Equal("server-model", options.Model);
        // PollingInterval was 1 (non-default) → server value NOT applied
        Assert.Equal(1, options.PollingIntervalSeconds);
        // Server-only repo appended
        Assert.Single(options.Repositories);
        Assert.Equal("ServerProject", options.Repositories[0].ProjectName);
    }

    [Fact]
    public async Task ExecuteCycle_ConfigChangesPropagate_OnNextCycle()
    {
        var mcpClient = new FakeMcpClient
        {
            ProfileToReturn = new AgentProfile
            {
                UserId = "agent-1",
                IsAgent = true,
                IsAgentActive = true,
                Configuration = new AgentProfileConfiguration
                {
                    Model = "first-model"
                }
            }
        };
        var options = CreateOptions();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = CreateLoop(options, mcpClient, logger);

        // First cycle: Model is null → server "first-model" applied
        await loop.ExecuteCycleAsync(CancellationToken.None);
        Assert.Equal("first-model", options.Model);

        // Update server config for second cycle
        mcpClient.ProfileToReturn = new AgentProfile
        {
            UserId = "agent-1",
            IsAgent = true,
            IsAgentActive = true,
            Configuration = new AgentProfileConfiguration
            {
                Model = "second-model"
            }
        };

        // Second cycle: Model is now "first-model" (non-null) → server "second-model" NOT applied (local wins)
        await loop.ExecuteCycleAsync(CancellationToken.None);
        Assert.Equal("first-model", options.Model);
    }
}
