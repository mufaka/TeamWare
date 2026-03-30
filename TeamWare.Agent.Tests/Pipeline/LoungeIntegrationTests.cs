using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Pipeline;

public class LoungeIntegrationTests
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
    public async Task ErrorTask_PostsCommentThenStatusThenLoungeMessage()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 5, Title = "Failing Task", Status = "ToDo", ProjectName = "Project Alpha", ProjectId = 42 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 5, Title = "Failing Task", Status = "ToDo" }
        };
        var copilotFactory = new AlwaysFailingFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Verify the sequence after pickup: error comment, Error status, lounge message
        // Find the index of the InProgress status change (pickup)
        var pickupIdx = mcpClient.Calls.FindIndex(c =>
            c.ToolName == "update_task_status" &&
            ((int, string))c.Args! == (5, "InProgress"));
        Assert.True(pickupIdx >= 0, "Pickup status change to InProgress not found");

        var postPickupCalls = mcpClient.Calls.Skip(pickupIdx + 1).ToList();

        // Find the error sequence
        var errorCommentCall = postPickupCalls.FirstOrDefault(c => c.ToolName == "add_comment");
        Assert.NotNull(errorCommentCall.ToolName);
        var (commentTaskId, commentContent) = ((int, string))errorCommentCall.Args!;
        Assert.Equal(5, commentTaskId);
        Assert.Contains("error occurred", commentContent);

        var errorStatusCall = postPickupCalls.FirstOrDefault(c =>
            c.ToolName == "update_task_status" && ((int, string))c.Args! is (5, "Error"));
        Assert.NotNull(errorStatusCall.ToolName);

        var loungeCall = postPickupCalls.FirstOrDefault(c => c.ToolName == "post_lounge_message");
        Assert.NotNull(loungeCall.ToolName);
        var (projectId, loungeContent) = ((int?, string))loungeCall.Args!;
        Assert.Equal(42, projectId); // project lounge, not global
        Assert.Contains("Task #5", loungeContent);
        Assert.Contains("Failing Task", loungeContent);
    }

    [Fact]
    public async Task ErrorTask_LoungeMessageIsPlainText()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 3, Title = "Bug Fix", Status = "ToDo", ProjectName = "Proj", ProjectId = 7 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 3, Title = "Bug Fix", Status = "ToDo" }
        };
        var copilotFactory = new AlwaysFailingFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        var loungeCall = mcpClient.Calls.FirstOrDefault(c => c.ToolName == "post_lounge_message");
        Assert.NotNull(loungeCall.ToolName);
        var (_, content) = ((int?, string))loungeCall.Args!;

        // CA-175: plain text only — no icons, emoticons, or decorative formatting
        Assert.DoesNotContain("🔴", content);
        Assert.DoesNotContain("❌", content);
        Assert.DoesNotContain("⚠", content);
        Assert.DoesNotContain("**", content);
        Assert.DoesNotContain("<", content);
    }

    [Fact]
    public async Task ErrorTask_LoungeMessageTargetsProjectLounge()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Task X", Status = "ToDo", ProjectName = "My Project", ProjectId = 99 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Task X", Status = "ToDo" }
        };
        var copilotFactory = new AlwaysFailingFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        var loungeCall = mcpClient.Calls.FirstOrDefault(c => c.ToolName == "post_lounge_message");
        Assert.NotNull(loungeCall.ToolName);
        var (projectId, _) = ((int?, string))loungeCall.Args!;

        // CA-178: must target the project lounge, not global (null)
        Assert.NotNull(projectId);
        Assert.Equal(99, projectId);
    }

    [Fact]
    public async Task SuccessfulTask_NoLoungeMessagePosted()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 1, Title = "Good Task", Status = "ToDo", ProjectName = "Proj", ProjectId = 5 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 1, Title = "Good Task", Status = "ToDo" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // CA-77: No lounge message for InReview transitions
        Assert.DoesNotContain(mcpClient.Calls, c => c.ToolName == "post_lounge_message");
    }

    [Fact]
    public async Task SuccessfulTask_PostsCommentAndTransitionsToInReview()
    {
        var mcpClient = new FakeMcpClient
        {
            AssignmentsToReturn =
            [
                new AgentTask { Id = 2, Title = "Feature A", Status = "ToDo", ProjectName = "Proj", ProjectId = 5 }
            ],
            TaskDetailToReturn = new AgentTaskDetail { Id = 2, Title = "Feature A", Status = "ToDo" }
        };
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var logger = new TestLogger<AgentPollingLoop>();
        var loop = new AgentPollingLoop(CreateOptions(), mcpClient, copilotFactory, logger);

        await loop.ExecuteCycleAsync(CancellationToken.None);

        // Verify completion: comment + InReview status
        var statusCalls = mcpClient.Calls
            .Where(c => c.ToolName == "update_task_status")
            .Select(c => ((int, string))c.Args!)
            .ToList();

        Assert.Contains(statusCalls, s => s.Item1 == 2 && s.Item2 == "InReview");

        var commentCalls = mcpClient.Calls
            .Where(c => c.ToolName == "add_comment")
            .Select(c => ((int, string))c.Args!)
            .ToList();

        // Should have pickup comment + completion comment
        Assert.True(commentCalls.Count >= 2);
        Assert.Contains(commentCalls, c => c.Item2.Contains("completed work on this task"));
    }

    [Fact]
    public void StatusTransitionHandler_BlockedLoungeMessage_MatchesFormat()
    {
        var message = StatusTransitionHandler.FormatBlockedLoungeMessage(42, "Implement Login");

        Assert.Equal(
            "I need help with Task #42 — Implement Login. I've posted a comment explaining what information I need. Can someone take a look?",
            message);
    }

    [Fact]
    public void StatusTransitionHandler_ErrorLoungeMessage_MatchesFormat()
    {
        var message = StatusTransitionHandler.FormatErrorLoungeMessage(7, "Fix CSS Bug");

        Assert.Equal(
            "I ran into a problem on Task #7 — Fix CSS Bug. I've posted a comment with the error details. Someone will need to triage this.",
            message);
    }

    [Fact]
    public async Task BlockTask_PostsCommentStatusAndLoungeMessage()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<StatusTransitionHandler>();
        var handler = new StatusTransitionHandler(mcpClient, logger);

        await handler.BlockTaskAsync(10, "Need clarification on requirements", "Design Homepage", 50);

        // Verify call sequence: comment, status, lounge
        Assert.True(mcpClient.Calls.Count >= 3);

        var commentCall = mcpClient.Calls.First(c => c.ToolName == "add_comment");
        var (commentTaskId, commentContent) = ((int, string))commentCall.Args!;
        Assert.Equal(10, commentTaskId);
        Assert.Equal("Need clarification on requirements", commentContent);

        var statusCall = mcpClient.Calls.First(c => c.ToolName == "update_task_status");
        var (statusTaskId, status) = ((int, string))statusCall.Args!;
        Assert.Equal(10, statusTaskId);
        Assert.Equal("Blocked", status);

        var loungeCall = mcpClient.Calls.First(c => c.ToolName == "post_lounge_message");
        var (projectId, loungeContent) = ((int?, string))loungeCall.Args!;
        Assert.Equal(50, projectId);
        Assert.Contains("Task #10", loungeContent);
        Assert.Contains("Design Homepage", loungeContent);
    }

    [Fact]
    public async Task BlockTask_LoungeMessageIsPlainText()
    {
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<StatusTransitionHandler>();
        var handler = new StatusTransitionHandler(mcpClient, logger);

        await handler.BlockTaskAsync(1, "Unclear scope", "Build API", 20);

        var loungeCall = mcpClient.Calls.First(c => c.ToolName == "post_lounge_message");
        var (_, content) = ((int?, string))loungeCall.Args!;

        Assert.DoesNotContain("🟡", content);
        Assert.DoesNotContain("⚠", content);
        Assert.DoesNotContain("**", content);
        Assert.DoesNotContain("<", content);
    }

    // --- Helper Classes ---

    private class AlwaysFailingFactory : ICopilotClientWrapperFactory
    {
        public ICopilotClientWrapper Create(AgentIdentityOptions options, Microsoft.Extensions.Logging.ILogger logger)
        {
            return new FakeCopilotClientWrapper { ThrowOnCreateSession = true };
        }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, string workingDirectory, Microsoft.Extensions.Logging.ILogger logger)
            => Create(options, logger);
    }
}
