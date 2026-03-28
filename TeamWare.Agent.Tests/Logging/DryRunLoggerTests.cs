using GitHub.Copilot.SDK;
using TeamWare.Agent.Logging;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Logging;

public class DryRunLoggerTests
{
    // 41.1 — Read operations are allowed

    [Fact]
    public async Task CreateHandler_ReadOnlyMcpTool_Approved()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        var request = PermissionRequestFactory.Mcp(toolName: "get_task", serverName: "teamware", readOnly: true);

        var result = await handler(request, default!);

        Assert.Equal(PermissionRequestResultKind.Approved, result.Kind);
    }

    // 41.1 — Write operations are blocked

    [Fact]
    public async Task CreateHandler_WriteMcpTool_Denied()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        var request = PermissionRequestFactory.Mcp(toolName: "update_task_status", serverName: "teamware", readOnly: false);

        var result = await handler(request, default!);

        Assert.Equal(PermissionRequestResultKind.DeniedByRules, result.Kind);
    }

    [Fact]
    public async Task CreateHandler_ShellCommand_Denied()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        var request = PermissionRequestFactory.Shell(fullCommandText: "dotnet build");

        var result = await handler(request, default!);

        Assert.Equal(PermissionRequestResultKind.DeniedByRules, result.Kind);
    }

    [Fact]
    public async Task CreateHandler_FileWrite_Denied()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        var request = PermissionRequestFactory.Write(fileName: "src/main.cs", intention: "Add a new method");

        var result = await handler(request, default!);

        Assert.Equal(PermissionRequestResultKind.DeniedByRules, result.Kind);
    }

    // 41.1 — Tool calls are logged

    [Fact]
    public async Task CreateHandler_DeniedShellCommand_LoggedWithDetails()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        await handler(PermissionRequestFactory.Shell(fullCommandText: "git commit -m 'fix'", intention: "Commit changes"), default!);

        Assert.Single(dryRunLogger.LoggedCalls);
        var call = dryRunLogger.LoggedCalls[0];
        Assert.Equal("Shell", call.Kind);
        Assert.Contains("git commit", call.Details);
        Assert.Equal("Commit changes", call.Intention);
    }

    [Fact]
    public async Task CreateHandler_DeniedMcpTool_LoggedWithDetails()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        await handler(PermissionRequestFactory.Mcp(toolName: "update_task_status", serverName: "teamware", readOnly: false), default!);

        Assert.Single(dryRunLogger.LoggedCalls);
        var call = dryRunLogger.LoggedCalls[0];
        Assert.Equal("MCP", call.Kind);
        Assert.Contains("teamware/update_task_status", call.Details);
    }

    [Fact]
    public async Task CreateHandler_DeniedFileWrite_LoggedWithDetails()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        await handler(PermissionRequestFactory.Write(fileName: "src/main.cs", intention: "Add method"), default!);

        Assert.Single(dryRunLogger.LoggedCalls);
        var call = dryRunLogger.LoggedCalls[0];
        Assert.Equal("Write", call.Kind);
        Assert.Contains("src/main.cs", call.Details);
        Assert.Equal("Add method", call.Intention);
    }

    [Fact]
    public async Task CreateHandler_ReadOperation_NotLoggedInCalls()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        await handler(PermissionRequestFactory.Mcp(toolName: "get_task", serverName: "teamware", readOnly: true), default!);

        Assert.Empty(dryRunLogger.LoggedCalls);
    }

    [Fact]
    public async Task CreateHandler_MultipleDeniedCalls_AllLogged()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        await handler(PermissionRequestFactory.Shell(fullCommandText: "dotnet build"), default!);
        await handler(PermissionRequestFactory.Write(fileName: "file.cs"), default!);
        await handler(PermissionRequestFactory.Mcp(toolName: "update", serverName: "mcp", readOnly: false), default!);

        Assert.Equal(3, dryRunLogger.LoggedCalls.Count);
    }

    // 41.1 — IsReadOnly logic

    [Fact]
    public void IsReadOnly_ReadOnlyMcp_ReturnsTrue()
    {
        var request = PermissionRequestFactory.Mcp(readOnly: true);
        Assert.True(DryRunLogger.IsReadOnly(request));
    }

    [Fact]
    public void IsReadOnly_WriteMcp_ReturnsFalse()
    {
        var request = PermissionRequestFactory.Mcp(readOnly: false);
        Assert.False(DryRunLogger.IsReadOnly(request));
    }

    [Fact]
    public void IsReadOnly_ShellCommand_ReturnsFalse()
    {
        var request = PermissionRequestFactory.Shell();
        Assert.False(DryRunLogger.IsReadOnly(request));
    }

    [Fact]
    public void IsReadOnly_FileWrite_ReturnsFalse()
    {
        var request = PermissionRequestFactory.Write();
        Assert.False(DryRunLogger.IsReadOnly(request));
    }

    // 41.1 — Logging output

    [Fact]
    public async Task CreateHandler_DeniedWrite_LogsInformation()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        await handler(PermissionRequestFactory.Shell(fullCommandText: "echo hello"), default!);

        Assert.Contains(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("[DRY RUN]") &&
            e.Message.Contains("write operation"));
    }

    [Fact]
    public async Task CreateHandler_ApprovedRead_LogsDebug()
    {
        var logger = new TestLogger<DryRunLogger>();
        var dryRunLogger = new DryRunLogger(logger);
        var handler = dryRunLogger.CreateHandler();

        await handler(PermissionRequestFactory.Mcp(readOnly: true, toolName: "get_task", serverName: "tw"), default!);

        Assert.Contains(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Debug &&
            e.Message.Contains("[DRY RUN]") &&
            e.Message.Contains("read operation"));
    }
}
