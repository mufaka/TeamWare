using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Tests;

public class FakeMcpClient : ITeamWareMcpClient
{
    public AgentProfile ProfileToReturn { get; set; } = new()
    {
        UserId = "test-user",
        DisplayName = "Test Agent",
        IsAgent = true,
        IsAgentActive = true
    };

    public List<AgentTask> AssignmentsToReturn { get; set; } = [];

    public AgentTaskDetail? TaskDetailToReturn { get; set; }

    public List<(string ToolName, object? Args)> Calls { get; } = [];

    public bool ThrowOnGetProfile { get; set; }
    public bool ThrowOnGetAssignments { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task<AgentProfile> GetMyProfileAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(("get_my_profile", null));

        if (ThrowOnGetProfile)
        {
            throw ExceptionToThrow ?? new HttpRequestException("Network error (simulated)");
        }

        return Task.FromResult(ProfileToReturn);
    }

    public Task<IReadOnlyList<AgentTask>> GetMyAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(("my_assignments", null));

        if (ThrowOnGetAssignments)
        {
            throw ExceptionToThrow ?? new HttpRequestException("Network error (simulated)");
        }

        return Task.FromResult<IReadOnlyList<AgentTask>>(AssignmentsToReturn);
    }

    public Task<AgentTaskDetail> GetTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        Calls.Add(("get_task", taskId));
        return Task.FromResult(TaskDetailToReturn ?? new AgentTaskDetail { Id = taskId });
    }

    public Task UpdateTaskStatusAsync(int taskId, string status, CancellationToken cancellationToken = default)
    {
        Calls.Add(("update_task_status", (taskId, status)));
        return Task.CompletedTask;
    }

    public Task AddCommentAsync(int taskId, string content, CancellationToken cancellationToken = default)
    {
        Calls.Add(("add_comment", (taskId, content)));
        return Task.CompletedTask;
    }

    public Task PostLoungeMessageAsync(int? projectId, string content, CancellationToken cancellationToken = default)
    {
        Calls.Add(("post_lounge_message", (projectId, content)));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
