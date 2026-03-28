using Microsoft.Extensions.Logging;
using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Pipeline;

/// <summary>
/// Handles all status transitions for agent-processed tasks.
/// Posts a comment before every status change and sends lounge
/// notifications for Blocked and Error transitions.
/// See Specification CA-60 through CA-66, CA-175 through CA-178.
/// </summary>
public class StatusTransitionHandler
{
    private readonly ITeamWareMcpClient _mcpClient;
    private readonly ILogger _logger;

    public StatusTransitionHandler(ITeamWareMcpClient mcpClient, ILogger logger)
    {
        _mcpClient = mcpClient;
        _logger = logger;
    }

    /// <summary>
    /// Picks up a task: posts a comment and transitions to InProgress (CA-60, CA-65).
    /// </summary>
    public async Task PickUpTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Picking up task #{TaskId}: posting comment and transitioning to InProgress", taskId);

        await _mcpClient.AddCommentAsync(taskId, "Starting work on this task.", cancellationToken);
        await _mcpClient.UpdateTaskStatusAsync(taskId, "InProgress", cancellationToken);

        _logger.LogInformation("Task #{TaskId} transitioned to InProgress", taskId);
    }

    /// <summary>
    /// Completes a task: posts a summary comment and transitions to InReview (CA-61, CA-65, CA-70).
    /// No lounge message is posted for InReview transitions (CA-77).
    /// </summary>
    public async Task CompleteTaskAsync(int taskId, string summary, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Completing task #{TaskId}: posting summary and transitioning to InReview", taskId);

        await _mcpClient.AddCommentAsync(taskId, summary, cancellationToken);
        await _mcpClient.UpdateTaskStatusAsync(taskId, "InReview", cancellationToken);

        _logger.LogInformation("Task #{TaskId} transitioned to InReview", taskId);
    }

    /// <summary>
    /// Blocks a task: posts a comment explaining the block, transitions to Blocked,
    /// and posts a lounge message to the project lounge (CA-63, CA-65, CA-71, CA-73, CA-176).
    /// </summary>
    public async Task BlockTaskAsync(int taskId, string reason, string taskTitle, int projectId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Blocking task #{TaskId}: posting comment and transitioning to Blocked", taskId);

        await _mcpClient.AddCommentAsync(taskId, reason, cancellationToken);
        await _mcpClient.UpdateTaskStatusAsync(taskId, "Blocked", cancellationToken);

        var loungeMessage = FormatBlockedLoungeMessage(taskId, taskTitle);
        await _mcpClient.PostLoungeMessageAsync(projectId, loungeMessage, cancellationToken);

        _logger.LogInformation("Task #{TaskId} transitioned to Blocked; lounge message posted to project {ProjectId}", taskId, projectId);
    }

    /// <summary>
    /// Errors a task: posts an error comment, transitions to Error,
    /// and posts a lounge message to the project lounge (CA-64, CA-65, CA-72, CA-74, CA-177).
    /// </summary>
    public async Task ErrorTaskAsync(int taskId, string errorDetails, string taskTitle, int projectId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Erroring task #{TaskId}: posting error details and transitioning to Error", taskId);

        await _mcpClient.AddCommentAsync(taskId, errorDetails, cancellationToken);
        await _mcpClient.UpdateTaskStatusAsync(taskId, "Error", cancellationToken);

        var loungeMessage = FormatErrorLoungeMessage(taskId, taskTitle);
        await _mcpClient.PostLoungeMessageAsync(projectId, loungeMessage, cancellationToken);

        _logger.LogInformation("Task #{TaskId} transitioned to Error; lounge message posted to project {ProjectId}", taskId, projectId);
    }

    /// <summary>
    /// Formats the lounge message for a Blocked task (CA-176).
    /// Plain text only — no icons, emoticons, or decorative formatting (CA-175).
    /// </summary>
    internal static string FormatBlockedLoungeMessage(int taskId, string taskTitle)
    {
        return $"I need help with Task #{taskId} — {taskTitle}. I've posted a comment explaining what information I need. Can someone take a look?";
    }

    /// <summary>
    /// Formats the lounge message for an Error task (CA-177).
    /// Plain text only — no icons, emoticons, or decorative formatting (CA-175).
    /// </summary>
    internal static string FormatErrorLoungeMessage(int taskId, string taskTitle)
    {
        return $"I ran into a problem on Task #{taskId} — {taskTitle}. I've posted a comment with the error details. Someone will need to triage this.";
    }
}
