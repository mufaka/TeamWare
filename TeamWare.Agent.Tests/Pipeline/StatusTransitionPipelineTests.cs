using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Pipeline;

public class StatusTransitionPipelineTests
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
    public async Task ExecuteCycle_PicksUpTaskBeforeProcessing()
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

        // Should have: get_my_profile, my_assignments, get_task (read-before-write),
        // add_comment (pickup), update_task_status (InProgress),
        // get_task (TaskProcessor), ... then add_comment (complete), update_task_status (InReview)
        var statusCalls = mcpClient.Calls
            .Where(c => c.ToolName == "update_task_status")
            .ToList();

        // First status change should be InProgress (pickup)
        Assert.True(statusCalls.Count >= 1);
        var (_, firstArgs) = statusCalls[0];
        var (taskId, status) = ((int, string))firstArgs!;
        Assert.Equal(1, taskId);
        Assert.Equal("InProgress", status);
    }

    [Fact]
    public async Task ExecuteCycle_SuccessfulTask_TransitionsToInReview()
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

        var statusCalls = mcpClient.Calls
            .Where(c => c.ToolName == "update_task_status")
            .ToList();

        // Should have two status changes: InProgress, then InReview
        Assert.Equal(2, statusCalls.Count);

        var (_, secondArgs) = statusCalls[1];
        var (taskId, status) = ((int, string))secondArgs!;
        Assert.Equal(1, taskId);
        Assert.Equal("InReview", status);
    }

    [Fact]
    public async Task ExecuteCycle_SuccessfulTask_PostsCompletionComment()
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

        var commentCalls = mcpClient.Calls
            .Where(c => c.ToolName == "add_comment")
            .ToList();

        // Should have two comments: pickup and completion
        Assert.Equal(2, commentCalls.Count);

        // First comment is the pickup comment
        var (_, firstArgs) = commentCalls[0];
        var (_, firstContent) = ((int, string))firstArgs!;
        Assert.Equal("Starting work on this task.", firstContent);

        // Second comment is the completion summary
        var (_, secondArgs) = commentCalls[1];
        var (_, secondContent) = ((int, string))secondArgs!;
        Assert.Contains("completed work on this task", secondContent);
    }

    [Fact]
    public async Task ExecuteCycle_SuccessfulTask_NoLoungeMessage()
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

        Assert.DoesNotContain(mcpClient.Calls, c => c.ToolName == "post_lounge_message");
    }

    [Fact]
    public async Task ExecuteCycle_ProcessingError_TransitionsToError()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo" }
        };
        var copilotFactory = new FailOnFirstTaskFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        var statusCalls = mcpClient.Calls
            .Where(c => c.ToolName == "update_task_status")
            .ToList();

        // Should have two status changes: InProgress (pickup), then Error (failure)
        Assert.Equal(2, statusCalls.Count);

        var (_, errorArgs) = statusCalls[1];
        var (taskId, status) = ((int, string))errorArgs!;
        Assert.Equal(1, taskId);
        Assert.Equal("Error", status);
    }

    [Fact]
    public async Task ExecuteCycle_ProcessingError_PostsErrorComment()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo" }
        };
        var copilotFactory = new FailOnFirstTaskFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        var commentCalls = mcpClient.Calls
            .Where(c => c.ToolName == "add_comment")
            .ToList();

        // Should have two comments: pickup + error details
        Assert.Equal(2, commentCalls.Count);

        var (_, errorArgs) = commentCalls[1];
        var (_, content) = ((int, string))errorArgs!;
        Assert.Contains("error occurred", content);
    }

    [Fact]
    public async Task ExecuteCycle_ProcessingError_PostsLoungeMessage()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo" }
        };
        var copilotFactory = new FailOnFirstTaskFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        var loungeCall = mcpClient.Calls.FirstOrDefault(c => c.ToolName == "post_lounge_message");
        Assert.NotNull(loungeCall.ToolName);
        var (projectId, content) = ((int?, string))loungeCall.Args!;
        Assert.Equal(10, projectId);
        Assert.Contains("Task #1", content);
    }

    [Fact]
    public async Task ExecuteCycle_TaskNotInToDo_Skipped()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            // Read-before-write returns InProgress — task was already picked up
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "InProgress" }
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
            e.Message.Contains("InProgress"));
    }

    [Fact]
    public async Task ExecuteCycle_TaskInInReview_Skipped()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "InReview" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal(0, copilotFactory.CreateCallCount);
        Assert.DoesNotContain(mcpClient.Calls, c => c.ToolName == "update_task_status");
    }

    [Fact]
    public async Task ExecuteCycle_TaskInBlocked_Skipped()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "Blocked" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal(0, copilotFactory.CreateCallCount);
    }

    [Fact]
    public async Task ExecuteCycle_TaskInError_Skipped()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "Error" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal(0, copilotFactory.CreateCallCount);
    }

    [Fact]
    public async Task ExecuteCycle_TaskInDone_Skipped()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "Done" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal(0, copilotFactory.CreateCallCount);
    }

    [Fact]
    public async Task ExecuteCycle_PickupComment_PostedBeforeCopilotSession()
    {
        var callOrder = new List<string>();
        var mcpClient = new OrderTrackingMcpClient(callOrder)
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task A", Status = "ToDo" }
        };
        var copilotFactory = new OrderTrackingCopilotFactory(callOrder);
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Verify ordering: pickup comment + status must happen before copilot session
        var commentIdx = callOrder.IndexOf("add_comment:pickup");
        var statusIdx = callOrder.IndexOf("update_task_status:InProgress");
        var copilotIdx = callOrder.IndexOf("copilot:create");

        Assert.True(commentIdx >= 0 && statusIdx >= 0 && copilotIdx >= 0,
            $"Expected all operations to occur. Actual order: {string.Join(", ", callOrder)}");
        Assert.True(commentIdx < statusIdx, "Comment should be posted before status change");
        Assert.True(statusIdx < copilotIdx, "Status change to InProgress should happen before Copilot session");
    }

    [Fact]
    public async Task ExecuteCycle_ErrorInOnTask_ContinuesToNextTask()
    {
        // First task will fail in processing, second should still succeed
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Will Fail", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 },
                new AgentTask { Id = 2, Title = "Will Succeed", Status = "ToDo", ProjectName = "Proj1", ProjectId = 10 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Will Fail", Status = "ToDo" }
        };
        // Override TaskDetailToReturn per call using a custom client
        var taskDetails = new Dictionary<int, AgentTaskDetail>
        {
            [1] = new() { Id = 1, Title = "Will Fail", Status = "ToDo" },
            [2] = new() { Id = 2, Title = "Will Succeed", Status = "ToDo" }
        };
        var multiTaskClient = new MultiTaskMcpClient(mcpClient, taskDetails);
        var copilotFactory = new FailOnFirstTaskFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), multiTaskClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Both tasks should have been attempted
        Assert.Equal(2, copilotFactory.CreateCallCount);

        // First task should have Error status
        var statusCalls = multiTaskClient.Calls
            .Where(c => c.ToolName == "update_task_status")
            .Select(c => ((int, string))c.Args!)
            .ToList();

        Assert.Contains(statusCalls, s => s.Item1 == 1 && s.Item2 == "Error");
        // Second task should have InReview status
        Assert.Contains(statusCalls, s => s.Item1 == 2 && s.Item2 == "InReview");
    }

    [Fact]
    public async Task ExecuteCycle_NoCopilotFactory_StillLogsPlaceholder()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task A", Status = "ToDo", ProjectName = "Proj1" }
            ]
        };
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Would process task #1"));

        // No status transitions when no copilot factory
        Assert.DoesNotContain(mcpClient.Calls, c => c.ToolName == "update_task_status");
    }

    // --- Helper Classes ---

    /// <summary>
    /// Tracks the ordering of MCP calls and Copilot session creation
    /// to verify pickup happens before processing.
    /// </summary>
    private class OrderTrackingMcpClient : ITeamWareMcpClient
    {
        private readonly List<string> _callOrder;
        private readonly FakeMcpClient _inner = new();
        private bool _pickupCommentPosted;

        public List<AgentTask> AssignmentsToReturn
        {
            get => _inner.AssignmentsToReturn;
            set => _inner.AssignmentsToReturn = value;
        }

        public AgentTaskDetail? TaskDetailToReturn
        {
            get => _inner.TaskDetailToReturn;
            set => _inner.TaskDetailToReturn = value;
        }

        public OrderTrackingMcpClient(List<string> callOrder)
        {
            _callOrder = callOrder;
        }

        public Task<AgentProfile> GetMyProfileAsync(CancellationToken ct = default)
            => _inner.GetMyProfileAsync(ct);

        public Task<IReadOnlyList<AgentTask>> GetMyAssignmentsAsync(CancellationToken ct = default)
            => _inner.GetMyAssignmentsAsync(ct);

        public Task<AgentTaskDetail> GetTaskAsync(int taskId, CancellationToken ct = default)
            => _inner.GetTaskAsync(taskId, ct);

        public Task UpdateTaskStatusAsync(int taskId, string status, CancellationToken ct = default)
        {
            _callOrder.Add($"update_task_status:{status}");
            return _inner.UpdateTaskStatusAsync(taskId, status, ct);
        }

        public Task AddCommentAsync(int taskId, string content, CancellationToken ct = default)
        {
            if (!_pickupCommentPosted)
            {
                _callOrder.Add("add_comment:pickup");
                _pickupCommentPosted = true;
            }
            else
            {
                _callOrder.Add("add_comment:completion");
            }
            return _inner.AddCommentAsync(taskId, content, ct);
        }

        public Task PostLoungeMessageAsync(int? projectId, string content, CancellationToken ct = default)
            => _inner.PostLoungeMessageAsync(projectId, content, ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private class OrderTrackingCopilotFactory : ICopilotClientWrapperFactory
    {
        private readonly List<string> _callOrder;

        public OrderTrackingCopilotFactory(List<string> callOrder)
        {
            _callOrder = callOrder;
        }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger)
        {
            _callOrder.Add("copilot:create");
            return new FakeCopilotClientWrapper();
        }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, string workingDirectory, ILogger logger)
            => Create(options, logger);
    }

    /// <summary>
    /// An MCP client that returns different task details per task ID,
    /// delegating everything else to a FakeMcpClient.
    /// </summary>
    private class MultiTaskMcpClient : ITeamWareMcpClient
    {
        private readonly FakeMcpClient _inner;
        private readonly Dictionary<int, AgentTaskDetail> _taskDetails;

        public List<(string ToolName, object? Args)> Calls => _inner.Calls;

        public MultiTaskMcpClient(FakeMcpClient inner, Dictionary<int, AgentTaskDetail> taskDetails)
        {
            _inner = inner;
            _taskDetails = taskDetails;
        }

        public Task<AgentProfile> GetMyProfileAsync(CancellationToken ct = default)
            => _inner.GetMyProfileAsync(ct);

        public Task<IReadOnlyList<AgentTask>> GetMyAssignmentsAsync(CancellationToken ct = default)
            => _inner.GetMyAssignmentsAsync(ct);

        public Task<AgentTaskDetail> GetTaskAsync(int taskId, CancellationToken ct = default)
        {
            _inner.Calls.Add(("get_task", taskId));
            if (_taskDetails.TryGetValue(taskId, out var detail))
                return Task.FromResult(detail);
            return Task.FromResult(new AgentTaskDetail { Id = taskId });
        }

        public Task UpdateTaskStatusAsync(int taskId, string status, CancellationToken ct = default)
            => _inner.UpdateTaskStatusAsync(taskId, status, ct);

        public Task AddCommentAsync(int taskId, string content, CancellationToken ct = default)
            => _inner.AddCommentAsync(taskId, content, ct);

        public Task PostLoungeMessageAsync(int? projectId, string content, CancellationToken ct = default)
            => _inner.PostLoungeMessageAsync(projectId, content, ct);

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
                return new FakeCopilotClientWrapper { ThrowOnCreateSession = true };
            }
            return new FakeCopilotClientWrapper();
        }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, string workingDirectory, ILogger logger)
            => Create(options, logger);
    }
}
