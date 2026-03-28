using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Pipeline;

public class MultipleIdentityTests
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
            PersonalAccessToken = $"pat-{name}",
            PollingIntervalSeconds = pollingInterval
        };
    }

    [Fact]
    public async Task TwoIdentities_PollIndependently()
    {
        var client1 = new FakeMcpClient
        {
            ProfileToReturn = new AgentProfile
            {
                UserId = "agent-1",
                DisplayName = "Agent 1",
                IsAgent = true,
                IsAgentActive = true
            },
            AssignmentsToReturn =
            [
                new AgentTask { Id = 100, Title = "Task for Agent 1", Status = "ToDo", ProjectName = "Project A", ProjectId = 1 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 100, Title = "Task for Agent 1", Status = "ToDo" }
        };

        var client2 = new FakeMcpClient
        {
            ProfileToReturn = new AgentProfile
            {
                UserId = "agent-2",
                DisplayName = "Agent 2",
                IsAgent = true,
                IsAgentActive = true
            },
            AssignmentsToReturn =
            [
                new AgentTask { Id = 200, Title = "Task for Agent 2", Status = "ToDo", ProjectName = "Project B", ProjectId = 2 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 200, Title = "Task for Agent 2", Status = "ToDo" }
        };

        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger1 = new TestLogger<AgentPollingLoop>();
        var logger2 = new TestLogger<AgentPollingLoop>();

        var loop1 = new AgentPollingLoop(
            CreateOptions("agent-1", workingDirectory: "/tmp/agent1"),
            client1, copilotFactory, logger1);
        var loop2 = new AgentPollingLoop(
            CreateOptions("agent-2", workingDirectory: "/tmp/agent2"),
            client2, copilotFactory, logger2);

        // Run both loops for a single cycle
        await Task.WhenAll(
            loop1.ExecuteCycleAsync(CancellationToken.None),
            loop2.ExecuteCycleAsync(CancellationToken.None));

        // Agent 1 should have processed task 100
        Assert.Contains(client1.Calls, c =>
            c.ToolName == "get_task" && c.Args is int id && id == 100);

        // Agent 2 should have processed task 200
        Assert.Contains(client2.Calls, c =>
            c.ToolName == "get_task" && c.Args is int id && id == 200);

        // Verify no cross-contamination: agent 1 didn't touch task 200
        Assert.DoesNotContain(client1.Calls, c =>
            c.ToolName == "get_task" && c.Args is int id && id == 200);

        // Agent 2 didn't touch task 100
        Assert.DoesNotContain(client2.Calls, c =>
            c.ToolName == "get_task" && c.Args is int id && id == 100);
    }

    [Fact]
    public async Task TwoIdentities_DoNotShareState()
    {
        var client1 = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo" }
        };

        var client2 = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 2, Title = "Task B", Status = "ToDo", ProjectName = "Proj2", ProjectId = 20 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 2, Title = "Task B", Status = "ToDo" }
        };

        var factory1 = new FakeCopilotClientWrapperFactory();
        var factory2 = new FakeCopilotClientWrapperFactory();
        var logger1 = new TestLogger<AgentPollingLoop>();
        var logger2 = new TestLogger<AgentPollingLoop>();

        var loop1 = new AgentPollingLoop(
            CreateOptions("identity-A", workingDirectory: "/tmp/A"),
            client1, factory1, logger1);
        var loop2 = new AgentPollingLoop(
            CreateOptions("identity-B", workingDirectory: "/tmp/B"),
            client2, factory2, logger2);

        await Task.WhenAll(
            loop1.ExecuteCycleAsync(CancellationToken.None),
            loop2.ExecuteCycleAsync(CancellationToken.None));

        // Each identity's MCP client should have been called independently
        Assert.Contains(client1.Calls, c => c.ToolName == "get_my_profile");
        Assert.Contains(client2.Calls, c => c.ToolName == "get_my_profile");

        // Each identity's Copilot factory should have been called
        Assert.Equal(1, factory1.CreateCallCount);
        Assert.Equal(1, factory2.CreateCallCount);
    }

    [Fact]
    public async Task OneIdentityFails_OtherContinues()
    {
        var failingClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Will Fail", Status = "ToDo", ProjectName = "P1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Will Fail", Status = "ToDo" }
        };

        var succeedingClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 2, Title = "Will Succeed", Status = "ToDo", ProjectName = "P2", ProjectId = 20 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 2, Title = "Will Succeed", Status = "ToDo" }
        };

        var failingFactory = new AlwaysFailingCopilotFactory();
        var succeedingFactory = new FakeCopilotClientWrapperFactory();
        var logger1 = new TestLogger<AgentPollingLoop>();
        var logger2 = new TestLogger<AgentPollingLoop>();

        var loop1 = new AgentPollingLoop(
            CreateOptions("failing-agent", workingDirectory: "/tmp/fail"),
            failingClient, failingFactory, logger1);
        var loop2 = new AgentPollingLoop(
            CreateOptions("succeeding-agent", workingDirectory: "/tmp/succeed"),
            succeedingClient, succeedingFactory, logger2);

        // Both should complete without throwing, even though one fails
        await Task.WhenAll(
            loop1.ExecuteCycleAsync(CancellationToken.None),
            loop2.ExecuteCycleAsync(CancellationToken.None));

        // Failing agent should have transitioned task to Error
        var failingStatusCalls = failingClient.Calls
            .Where(c => c.ToolName == "update_task_status")
            .Select(c => ((int, string))c.Args!)
            .ToList();
        Assert.Contains(failingStatusCalls, s => s.Item1 == 1 && s.Item2 == "Error");

        // Succeeding agent should have transitioned task to InReview
        var succeedingStatusCalls = succeedingClient.Calls
            .Where(c => c.ToolName == "update_task_status")
            .Select(c => ((int, string))c.Args!)
            .ToList();
        Assert.Contains(succeedingStatusCalls, s => s.Item1 == 2 && s.Item2 == "InReview");
    }

    [Fact]
    public async Task TwoIdentities_DifferentPollingIntervals_RunIndependently()
    {
        var client1 = new FakeMcpClient();
        var client2 = new FakeMcpClient();
        var logger1 = new TestLogger<AgentPollingLoop>();
        var logger2 = new TestLogger<AgentPollingLoop>();

        // Different polling intervals
        var loop1 = new AgentPollingLoop(
            CreateOptions("fast-agent", pollingInterval: 1, workingDirectory: "/tmp/fast"),
            client1, logger1);
        var loop2 = new AgentPollingLoop(
            CreateOptions("slow-agent", pollingInterval: 5, workingDirectory: "/tmp/slow"),
            client2, logger2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Run both; fast agent should poll more times
        var task1 = loop1.RunAsync(cts.Token);
        var task2 = loop2.RunAsync(cts.Token);
        await Task.WhenAll(task1, task2);

        var fast = client1.Calls.Count(c => c.ToolName == "get_my_profile");
        var slow = client2.Calls.Count(c => c.ToolName == "get_my_profile");

        // Fast agent (1s interval) should have polled more than slow (5s interval)
        Assert.True(fast >= slow,
            $"Fast agent polled {fast} times, slow agent polled {slow} times — expected fast >= slow");
    }

    [Fact]
    public async Task HostedService_TwoIdentities_BothGetPollingLoops()
    {
        var agents = new List<AgentIdentityOptions>
        {
            CreateOptions("hosted-agent-1", workingDirectory: "/tmp/hosted1"),
            CreateOptions("hosted-agent-2", workingDirectory: "/tmp/hosted2")
        };

        var logger = new TestLogger<AgentHostedService>();
        var factory = new FakeMcpClientFactory();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var loggerFactory = new TestLoggerFactory(logger);

        var service = new AgentHostedService(
            Options.Create(agents),
            factory,
            copilotFactory,
            loggerFactory,
            logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        // Both agents should have started
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("'hosted-agent-1'") &&
            e.Message.Contains("Starting polling loop"));
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("'hosted-agent-2'") &&
            e.Message.Contains("Starting polling loop"));
    }

    [Fact]
    public async Task HostedService_OneIdentityFailsCreation_OtherStillRuns()
    {
        var agents = new List<AgentIdentityOptions>
        {
            CreateOptions("failing-create", workingDirectory: "/tmp/fail"),
            CreateOptions("succeeding-create", workingDirectory: "/tmp/succeed")
        };

        var callCount = 0;
        var factory = new FakeMcpClientFactory
        {
            ClientFactory = options =>
            {
                callCount++;
                if (options.Name == "failing-create")
                {
                    throw new InvalidOperationException("Simulated creation failure");
                }
                return new FakeMcpClient();
            }
        };

        var logger = new TestLogger<AgentHostedService>();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var loggerFactory = new TestLoggerFactory(logger);

        var service = new AgentHostedService(
            Options.Create(agents),
            factory,
            copilotFactory,
            loggerFactory,
            logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        // Failing identity should have logged an error
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("'failing-create'"));

        // Succeeding identity should have started its polling loop
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop started") &&
            e.Message.Contains("'succeeding-create'"));
    }

    // --- Helper Classes ---

    private class AlwaysFailingCopilotFactory : ICopilotClientWrapperFactory
    {
        public ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger)
        {
            return new FakeCopilotClientWrapper { ThrowOnCreateSession = true };
        }
    }
}
