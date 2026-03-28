using GitHub.Copilot.SDK;
using TeamWare.Agent.Permissions;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Permissions;

public class AgentPermissionHandlerTests
{
    private static AgentPermissionHandler CreateHandler()
    {
        var logger = new TestLogger<AgentPermissionHandler>();
        return new AgentPermissionHandler(logger);
    }

    // 41.2 — Custom handler approves normal operations

    [Fact]
    public void Evaluate_NonShellRequest_Approved()
    {
        var handler = CreateHandler();
        var request = PermissionRequestFactory.Mcp(toolName: "get_task", serverName: "teamware", readOnly: true);

        var (approved, reason) = handler.Evaluate(request);

        Assert.True(approved);
        Assert.Null(reason);
    }

    [Fact]
    public void Evaluate_McpWriteTool_Approved()
    {
        var handler = CreateHandler();
        var request = PermissionRequestFactory.Mcp(toolName: "update_task_status", serverName: "teamware", readOnly: false);

        var (approved, reason) = handler.Evaluate(request);

        Assert.True(approved);
        Assert.Null(reason);
    }

    [Fact]
    public void Evaluate_WriteRequest_Approved()
    {
        var handler = CreateHandler();
        var request = PermissionRequestFactory.Write(fileName: "src/main.cs", intention: "Add a new method");

        var (approved, reason) = handler.Evaluate(request);

        Assert.True(approved);
        Assert.Null(reason);
    }

    [Fact]
    public void Evaluate_SafeShellCommand_Approved()
    {
        var handler = CreateHandler();
        var request = PermissionRequestFactory.Shell(fullCommandText: "dotnet build");

        var (approved, reason) = handler.Evaluate(request);

        Assert.True(approved);
        Assert.Null(reason);
    }

    [Fact]
    public void Evaluate_EmptyShellCommand_Approved()
    {
        var handler = CreateHandler();
        var request = PermissionRequestFactory.Shell(fullCommandText: "");

        var (approved, reason) = handler.Evaluate(request);

        Assert.True(approved);
        Assert.Null(reason);
    }

    [Fact]
    public void Evaluate_NullShellCommand_Approved()
    {
        var handler = CreateHandler();
        var request = PermissionRequestFactory.Shell(fullCommandText: null);

        var (approved, reason) = handler.Evaluate(request);

        Assert.True(approved);
        Assert.Null(reason);
    }

    // 41.2 — Custom handler denies dangerous shell commands

    [Theory]
    [InlineData("rm -rf /tmp/something")]
    [InlineData("rm -r /")]
    [InlineData("git push --force origin main")]
    [InlineData("git push -f origin feature")]
    [InlineData("git checkout main")]
    [InlineData("git checkout master")]
    [InlineData("git merge main")]
    [InlineData("git merge master")]
    public void Evaluate_DangerousShellCommand_Denied(string command)
    {
        var handler = CreateHandler();
        var request = PermissionRequestFactory.Shell(fullCommandText: command);

        var (approved, reason) = handler.Evaluate(request);

        Assert.False(approved);
        Assert.NotNull(reason);
        Assert.Contains("Dangerous shell command blocked", reason);
    }

    [Fact]
    public void Evaluate_DangerousCommand_CaseInsensitive()
    {
        var handler = CreateHandler();
        var request = PermissionRequestFactory.Shell(fullCommandText: "GIT PUSH --FORCE origin main");

        var (approved, reason) = handler.Evaluate(request);

        Assert.False(approved);
    }

    // 41.2 — CreateHandler returns a working delegate

    [Fact]
    public async Task CreateHandler_SafeCommand_ReturnsApproved()
    {
        var handler = CreateHandler();
        var del = handler.CreateHandler();
        var request = PermissionRequestFactory.Shell(fullCommandText: "dotnet test");

        var result = await del(request, default!);

        Assert.Equal(PermissionRequestResultKind.Approved, result.Kind);
    }

    [Fact]
    public async Task CreateHandler_DangerousCommand_ReturnsDenied()
    {
        var handler = CreateHandler();
        var del = handler.CreateHandler();
        var request = PermissionRequestFactory.Shell(fullCommandText: "rm -rf /");

        var result = await del(request, default!);

        Assert.Equal(PermissionRequestResultKind.DeniedByRules, result.Kind);
    }

    // 41.2 — Logging

    [Fact]
    public async Task CreateHandler_ApprovedCommand_LogsDebug()
    {
        var logger = new TestLogger<AgentPermissionHandler>();
        var handler = new AgentPermissionHandler(logger);
        var del = handler.CreateHandler();

        await del(PermissionRequestFactory.Shell(fullCommandText: "dotnet build"), default!);

        Assert.Contains(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Debug &&
            e.Message.Contains("APPROVED"));
    }

    [Fact]
    public async Task CreateHandler_DeniedCommand_LogsWarning()
    {
        var logger = new TestLogger<AgentPermissionHandler>();
        var handler = new AgentPermissionHandler(logger);
        var del = handler.CreateHandler();

        await del(PermissionRequestFactory.Shell(fullCommandText: "rm -rf /"), default!);

        Assert.Contains(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("DENIED"));
    }
}
