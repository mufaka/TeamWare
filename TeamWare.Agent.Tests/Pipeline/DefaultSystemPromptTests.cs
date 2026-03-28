namespace TeamWare.Agent.Tests.Pipeline;

public class DefaultSystemPromptTests
{
    [Fact]
    public void Text_IsNotNullOrEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(Agent.Pipeline.DefaultSystemPrompt.Text));
    }

    [Fact]
    public void Text_ContainsAllEightSteps()
    {
        var text = Agent.Pipeline.DefaultSystemPrompt.Text;

        Assert.Contains("Read the task details", text);
        Assert.Contains("Assess the scope", text);
        Assert.Contains("Explore the codebase", text);
        Assert.Contains("minimal, targeted changes", text);
        Assert.Contains("build and test commands", text);
        Assert.Contains("Commit your changes", text);
        Assert.Contains("Post a comment on the task", text);
        Assert.Contains("Update the task status to InReview", text);
    }

    [Fact]
    public void Text_ContainsAllRules()
    {
        var text = Agent.Pipeline.DefaultSystemPrompt.Text;

        Assert.Contains("Never set a task to Done", text);
        Assert.Contains("Never create or delete tasks", text);
        Assert.Contains("Never reassign tasks", text);
        Assert.Contains("Never delete comments", text);
        Assert.Contains("Always post a comment before changing task status", text);
        Assert.Contains("agent/<task-id>", text);
        Assert.Contains("task is unclear", text);
        Assert.Contains("task is too large", text);
    }

    [Fact]
    public void Text_ContainsBlockedStatusInstructions()
    {
        var text = Agent.Pipeline.DefaultSystemPrompt.Text;

        Assert.Contains("Blocked", text);
    }

    [Fact]
    public void Text_NeverMentionsDoneAsAgentAction()
    {
        var text = Agent.Pipeline.DefaultSystemPrompt.Text;

        // The prompt says "Never set a task to Done" and "Only humans approve work"
        Assert.Contains("Only humans approve work", text);
    }
}
