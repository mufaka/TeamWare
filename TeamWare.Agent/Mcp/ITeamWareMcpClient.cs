namespace TeamWare.Agent.Mcp;

public interface ITeamWareMcpClient : IAsyncDisposable
{
    Task<AgentProfile> GetMyProfileAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentTask>> GetMyAssignmentsAsync(CancellationToken cancellationToken = default);
    Task<AgentTaskDetail> GetTaskAsync(int taskId, CancellationToken cancellationToken = default);
    Task UpdateTaskStatusAsync(int taskId, string status, CancellationToken cancellationToken = default);
    Task AddCommentAsync(int taskId, string content, CancellationToken cancellationToken = default);
    Task PostLoungeMessageAsync(int? projectId, string content, CancellationToken cancellationToken = default);
}
