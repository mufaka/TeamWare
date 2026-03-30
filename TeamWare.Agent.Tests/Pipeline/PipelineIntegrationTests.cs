using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Pipeline;

public class PipelineIntegrationTests
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

    [Fact]
    public async Task ExecuteCycle_WithCopilotFactory_ProcessesTasks()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Should have created a Copilot client
        Assert.Equal(1, copilotFactory.CreateCallCount);
        // Should have fetched task details
        Assert.Contains(mcpClient.Calls, c => c.ToolName == "get_task" && c.Args is int id && id == 1);
    }

    [Fact]
    public async Task ExecuteCycle_ProcessesTasksSequentially()
    {
        var processingOrder = new List<int>();
        var mcpClient = new TrackingMcpClient(processingOrder)
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 10, Title = "First", Status = "ToDo", ProjectName = "Proj1" },
                new AgentTask { Id = 20, Title = "Second", Status = "ToDo", ProjectName = "Proj1" },
                new AgentTask { Id = 30, Title = "Third", Status = "ToDo", ProjectName = "Proj1" }
            ]
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Tasks should be processed in order (each task gets multiple GetTaskAsync calls
        // due to read-before-write + TaskProcessor, so deduplicate to verify ordering)
        var distinctOrder = processingOrder.Distinct().ToList();
        Assert.Equal([10, 20, 30], distinctOrder);
    }

    [Fact]
    public async Task ExecuteCycle_ErrorInOneTask_ContinuesToNextTask()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Will Fail", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 },
                new AgentTask { Id = 2, Title = "Will Succeed", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Will Fail", Status = "ToDo" }
        };
        // Factory that fails on first task, succeeds on second
        var copilotFactory = new FailOnFirstTaskFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Should have attempted both tasks
        Assert.Equal(2, copilotFactory.CreateCallCount);

        // Should have logged error for first task
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error processing task #1"));

        // Second task should have been processed
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Copilot session completed for task #2"));
    }

    [Fact]
    public async Task ExecuteCycle_NoCopilotFactory_LogsPlaceholder()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1" }
            ]
        };
        var logger = new TestLogger<AgentPollingLoop>();
        // Use the constructor without CopilotFactory
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Should log the placeholder message
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Would process task #1"));
    }

    [Fact]
    public async Task RunAsync_ClientCreationFails_LogsErrorAndContinuesLoop()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory { ThrowOnCreate = true };
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(pollingInterval: 1), mcpClient, copilotFactory, logger);

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);

        await Task.Delay(1500);
        await cts.CancelAsync();
        await loopTask;

        // Error should be logged but loop continues
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error processing task #1"));

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Polling loop stopped"));
    }

    // Helper classes

    private class TrackingMcpClient : ITeamWareMcpClient
    {
        private readonly List<int> _processingOrder;
        private readonly FakeMcpClient _inner = new();

        public List<AgentTask> AssignmentsToReturn
        {
            get => _inner.AssignmentsToReturn;
            set => _inner.AssignmentsToReturn = value;
        }

        public TrackingMcpClient(List<int> processingOrder)
        {
            _processingOrder = processingOrder;
        }

        public Task<AgentProfile> GetMyProfileAsync(CancellationToken cancellationToken = default)
            => _inner.GetMyProfileAsync(cancellationToken);

        public Task<IReadOnlyList<AgentTask>> GetMyAssignmentsAsync(CancellationToken cancellationToken = default)
            => _inner.GetMyAssignmentsAsync(cancellationToken);

        public Task<AgentTaskDetail> GetTaskAsync(int taskId, CancellationToken cancellationToken = default)
        {
            _processingOrder.Add(taskId);
            return Task.FromResult(new AgentTaskDetail { Id = taskId, Title = $"Task {taskId}", Status = "ToDo" });
        }

        public Task UpdateTaskStatusAsync(int taskId, string status, CancellationToken cancellationToken = default)
            => _inner.UpdateTaskStatusAsync(taskId, status, cancellationToken);

        public Task AddCommentAsync(int taskId, string content, CancellationToken cancellationToken = default)
            => _inner.AddCommentAsync(taskId, content, cancellationToken);

        public Task PostLoungeMessageAsync(int? projectId, string content, CancellationToken cancellationToken = default)
            => _inner.PostLoungeMessageAsync(projectId, content, cancellationToken);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private class FailOnFirstTaskFactory : ICopilotClientWrapperFactory
    {
        public int CreateCallCount { get; private set; }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger)
        {
            CreateCallCount++;
            if (CreateCallCount == 1)
            {
                // Return a client whose session throws
                return new FailingSessionClient();
            }

            return new FakeCopilotClientWrapper();
        }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, string workingDirectory, ILogger logger)
            => Create(options, logger);

        private class FailingSessionClient : ICopilotClientWrapper
        {
            public Task StartAsync() => Task.CompletedTask;

            public Task<ICopilotSessionWrapper> CreateSessionAsync(SessionConfig config)
            {
                return Task.FromResult<ICopilotSessionWrapper>(
                    new FakeCopilotSessionWrapper { ThrowOnSendAndWait = true });
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
