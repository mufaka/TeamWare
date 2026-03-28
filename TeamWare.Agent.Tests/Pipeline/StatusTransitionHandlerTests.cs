using Microsoft.Extensions.Logging;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Pipeline;

public class StatusTransitionHandlerTests
{
    private readonly FakeMcpClient _mcpClient = new();
    private readonly TestLogger<StatusTransitionHandler> _logger = new();
    private readonly StatusTransitionHandler _handler;

    public StatusTransitionHandlerTests()
    {
        _handler = new StatusTransitionHandler(_mcpClient, _logger);
    }

    // --- PickUpTaskAsync ---

    [Fact]
    public async Task PickUpTask_PostsCommentBeforeStatusChange()
    {
        await _handler.PickUpTaskAsync(42);

        Assert.Equal(2, _mcpClient.Calls.Count);
        Assert.Equal("add_comment", _mcpClient.Calls[0].ToolName);
        Assert.Equal("update_task_status", _mcpClient.Calls[1].ToolName);
    }

    [Fact]
    public async Task PickUpTask_PostsCorrectComment()
    {
        await _handler.PickUpTaskAsync(42);

        var (_, args) = _mcpClient.Calls[0];
        var (taskId, content) = ((int, string))args!;
        Assert.Equal(42, taskId);
        Assert.Equal("Starting work on this task.", content);
    }

    [Fact]
    public async Task PickUpTask_TransitionsToInProgress()
    {
        await _handler.PickUpTaskAsync(42);

        var (_, args) = _mcpClient.Calls[1];
        var (taskId, status) = ((int, string))args!;
        Assert.Equal(42, taskId);
        Assert.Equal("InProgress", status);
    }

    // --- CompleteTaskAsync ---

    [Fact]
    public async Task CompleteTask_PostsCommentBeforeStatusChange()
    {
        await _handler.CompleteTaskAsync(42, "All done.");

        Assert.Equal(2, _mcpClient.Calls.Count);
        Assert.Equal("add_comment", _mcpClient.Calls[0].ToolName);
        Assert.Equal("update_task_status", _mcpClient.Calls[1].ToolName);
    }

    [Fact]
    public async Task CompleteTask_PostsSummaryComment()
    {
        await _handler.CompleteTaskAsync(42, "All done.");

        var (_, args) = _mcpClient.Calls[0];
        var (taskId, content) = ((int, string))args!;
        Assert.Equal(42, taskId);
        Assert.Equal("All done.", content);
    }

    [Fact]
    public async Task CompleteTask_TransitionsToInReview()
    {
        await _handler.CompleteTaskAsync(42, "All done.");

        var (_, args) = _mcpClient.Calls[1];
        var (taskId, status) = ((int, string))args!;
        Assert.Equal(42, taskId);
        Assert.Equal("InReview", status);
    }

    [Fact]
    public async Task CompleteTask_NoLoungeMessage()
    {
        await _handler.CompleteTaskAsync(42, "All done.");

        Assert.DoesNotContain(_mcpClient.Calls, c => c.ToolName == "post_lounge_message");
    }

    // --- BlockTaskAsync ---

    [Fact]
    public async Task BlockTask_PostsCommentBeforeStatusChange()
    {
        await _handler.BlockTaskAsync(42, "Need clarification.", "My Task", 10);

        Assert.Equal("add_comment", _mcpClient.Calls[0].ToolName);
        Assert.Equal("update_task_status", _mcpClient.Calls[1].ToolName);
    }

    [Fact]
    public async Task BlockTask_PostsReasonAsComment()
    {
        await _handler.BlockTaskAsync(42, "Need clarification.", "My Task", 10);

        var (_, args) = _mcpClient.Calls[0];
        var (taskId, content) = ((int, string))args!;
        Assert.Equal(42, taskId);
        Assert.Equal("Need clarification.", content);
    }

    [Fact]
    public async Task BlockTask_TransitionsToBlocked()
    {
        await _handler.BlockTaskAsync(42, "Need clarification.", "My Task", 10);

        var (_, args) = _mcpClient.Calls[1];
        var (taskId, status) = ((int, string))args!;
        Assert.Equal(42, taskId);
        Assert.Equal("Blocked", status);
    }

    [Fact]
    public async Task BlockTask_PostsLoungeMessageToProjectLounge()
    {
        await _handler.BlockTaskAsync(42, "Need clarification.", "My Task", 10);

        var loungeCall = _mcpClient.Calls.FirstOrDefault(c => c.ToolName == "post_lounge_message");
        Assert.NotNull(loungeCall.ToolName);
        var (projectId, content) = ((int?, string))loungeCall.Args!;
        Assert.Equal(10, projectId);
        Assert.Contains("Task #42", content);
        Assert.Contains("My Task", content);
    }

    [Fact]
    public async Task BlockTask_LoungeMessageMatchesSpecFormat()
    {
        await _handler.BlockTaskAsync(42, "Need clarification.", "Fix login bug", 10);

        var loungeCall = _mcpClient.Calls.First(c => c.ToolName == "post_lounge_message");
        var (_, content) = ((int?, string))loungeCall.Args!;
        Assert.Equal(
            "I need help with Task #42 — Fix login bug. I've posted a comment explaining what information I need. Can someone take a look?",
            content);
    }

    // --- ErrorTaskAsync ---

    [Fact]
    public async Task ErrorTask_PostsCommentBeforeStatusChange()
    {
        await _handler.ErrorTaskAsync(42, "Build failed.", "My Task", 10);

        Assert.Equal("add_comment", _mcpClient.Calls[0].ToolName);
        Assert.Equal("update_task_status", _mcpClient.Calls[1].ToolName);
    }

    [Fact]
    public async Task ErrorTask_PostsErrorDetailsAsComment()
    {
        await _handler.ErrorTaskAsync(42, "Build failed.", "My Task", 10);

        var (_, args) = _mcpClient.Calls[0];
        var (taskId, content) = ((int, string))args!;
        Assert.Equal(42, taskId);
        Assert.Equal("Build failed.", content);
    }

    [Fact]
    public async Task ErrorTask_TransitionsToError()
    {
        await _handler.ErrorTaskAsync(42, "Build failed.", "My Task", 10);

        var (_, args) = _mcpClient.Calls[1];
        var (taskId, status) = ((int, string))args!;
        Assert.Equal(42, taskId);
        Assert.Equal("Error", status);
    }

    [Fact]
    public async Task ErrorTask_PostsLoungeMessageToProjectLounge()
    {
        await _handler.ErrorTaskAsync(42, "Build failed.", "My Task", 10);

        var loungeCall = _mcpClient.Calls.FirstOrDefault(c => c.ToolName == "post_lounge_message");
        Assert.NotNull(loungeCall.ToolName);
        var (projectId, content) = ((int?, string))loungeCall.Args!;
        Assert.Equal(10, projectId);
        Assert.Contains("Task #42", content);
        Assert.Contains("My Task", content);
    }

    [Fact]
    public async Task ErrorTask_LoungeMessageMatchesSpecFormat()
    {
        await _handler.ErrorTaskAsync(42, "Build failed.", "Fix login bug", 10);

        var loungeCall = _mcpClient.Calls.First(c => c.ToolName == "post_lounge_message");
        var (_, content) = ((int?, string))loungeCall.Args!;
        Assert.Equal(
            "I ran into a problem on Task #42 — Fix login bug. I've posted a comment with the error details. Someone will need to triage this.",
            content);
    }

    // --- Lounge Message Formatting ---

    [Fact]
    public void FormatBlockedLoungeMessage_ReturnsPlainText()
    {
        var message = StatusTransitionHandler.FormatBlockedLoungeMessage(42, "Fix login bug");

        Assert.Equal(
            "I need help with Task #42 — Fix login bug. I've posted a comment explaining what information I need. Can someone take a look?",
            message);
    }

    [Fact]
    public void FormatErrorLoungeMessage_ReturnsPlainText()
    {
        var message = StatusTransitionHandler.FormatErrorLoungeMessage(42, "Fix login bug");

        Assert.Equal(
            "I ran into a problem on Task #42 — Fix login bug. I've posted a comment with the error details. Someone will need to triage this.",
            message);
    }

    // --- Status Transition Correctness ---

    [Fact]
    public async Task AllMethods_PostCommentBeforeEveryStatusChange()
    {
        // Each method should post a comment at index 0 and status change at index 1
        await _handler.PickUpTaskAsync(1);
        Assert.Equal("add_comment", _mcpClient.Calls[0].ToolName);
        Assert.Equal("update_task_status", _mcpClient.Calls[1].ToolName);

        _mcpClient.Calls.Clear();
        await _handler.CompleteTaskAsync(2, "Done.");
        Assert.Equal("add_comment", _mcpClient.Calls[0].ToolName);
        Assert.Equal("update_task_status", _mcpClient.Calls[1].ToolName);

        _mcpClient.Calls.Clear();
        await _handler.BlockTaskAsync(3, "Blocked.", "T", 10);
        Assert.Equal("add_comment", _mcpClient.Calls[0].ToolName);
        Assert.Equal("update_task_status", _mcpClient.Calls[1].ToolName);

        _mcpClient.Calls.Clear();
        await _handler.ErrorTaskAsync(4, "Error.", "T", 10);
        Assert.Equal("add_comment", _mcpClient.Calls[0].ToolName);
        Assert.Equal("update_task_status", _mcpClient.Calls[1].ToolName);
    }
}
