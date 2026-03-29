using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Logging;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent.Tests.Pipeline;

public class TaskProcessorTests
{
    private static AgentIdentityOptions CreateOptions(
        string name = "test-agent",
        string? model = null,
        bool autoApproveTools = true,
        bool dryRun = false,
        string? systemPrompt = null)
    {
        return new AgentIdentityOptions
        {
            Name = name,
            WorkingDirectory = "/tmp/test",
            PersonalAccessToken = "test-pat",
            Model = model,
            AutoApproveTools = autoApproveTools,
            DryRun = dryRun,
            SystemPrompt = systemPrompt
        };
    }

    private static AgentTask CreateTask(
        int id = 42,
        string title = "Fix the bug",
        string status = "ToDo",
        string priority = "Medium",
        string projectName = "TestProject")
    {
        return new AgentTask
        {
            Id = id,
            Title = title,
            Status = status,
            Priority = priority,
            ProjectName = projectName,
            ProjectId = 1
        };
    }

    // 39.1 Tests — Session creation and task prompt

    [Fact]
    public async Task ProcessAsync_CreatesClientAndSession()
    {
        var options = CreateOptions();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient
        {
            TaskDetailToReturn = new AgentTaskDetail
            {
                Id = 42,
                Title = "Fix the bug",
                Description = "There is a bug in the login",
                Status = "ToDo",
                Priority = "Medium"
            }
        };
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        Assert.Equal(1, copilotFactory.CreateCallCount);
        Assert.NotNull(copilotFactory.LastCreatedClient);
        Assert.True(copilotFactory.LastCreatedClient.StartCalled);
        Assert.NotNull(copilotFactory.LastCreatedClient.LastCreatedSession);
        Assert.Equal(1, copilotFactory.LastCreatedClient.LastCreatedSession.SendAndWaitCallCount);
    }

    [Fact]
    public async Task ProcessAsync_FetchesTaskDetails()
    {
        var options = CreateOptions();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient
        {
            TaskDetailToReturn = new AgentTaskDetail
            {
                Id = 42,
                Title = "Fix the bug",
                Status = "ToDo",
                Priority = "Medium"
            }
        };
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        Assert.Contains(mcpClient.Calls, c => c.ToolName == "get_task" && c.Args is int id && id == 42);
    }

    [Fact]
    public async Task ProcessAsync_DisposesClientAndSession()
    {
        var options = CreateOptions();
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        Assert.True(copilotFactory.LastCreatedClient!.DisposeCalled);
        Assert.True(copilotFactory.LastCreatedClient.LastCreatedSession!.DisposeCalled);
    }

    [Fact]
    public async Task ProcessAsync_SessionConfigUsesModel()
    {
        var options = CreateOptions(model: "gpt-5");
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        var config = copilotFactory.LastCreatedClient!.LastSessionConfig!;
        Assert.Equal("gpt-5", config.Model);
    }

    [Fact]
    public async Task ProcessAsync_SessionConfigUsesDefaultSystemPrompt()
    {
        var options = CreateOptions(systemPrompt: null);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        var config = copilotFactory.LastCreatedClient!.LastSessionConfig!;
        Assert.Equal(SystemMessageMode.Append, config.SystemMessage!.Mode);
        Assert.Equal(DefaultSystemPrompt.Text, config.SystemMessage.Content);
    }

    [Fact]
    public async Task ProcessAsync_SessionConfigUsesCustomSystemPrompt()
    {
        var customPrompt = "You are a helpful coding assistant for documentation tasks.";
        var options = CreateOptions(systemPrompt: customPrompt);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        var config = copilotFactory.LastCreatedClient!.LastSessionConfig!;
        Assert.Equal(SystemMessageMode.Append, config.SystemMessage!.Mode);
        Assert.Equal(customPrompt, config.SystemMessage.Content);
    }

    [Fact]
    public async Task ProcessAsync_SessionConfigUsesApproveAllWhenAutoApprove()
    {
        var options = CreateOptions(autoApproveTools: true);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        var config = copilotFactory.LastCreatedClient!.LastSessionConfig!;
        Assert.NotNull(config.OnPermissionRequest);
        // PermissionHandler.ApproveAll is used — it's the same delegate
        Assert.Equal(PermissionHandler.ApproveAll, config.OnPermissionRequest);
    }

    [Fact]
    public async Task ProcessAsync_SessionConfigUsesCustomHandlerWhenNotAutoApprove()
    {
        var options = CreateOptions(autoApproveTools: false);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        var config = copilotFactory.LastCreatedClient!.LastSessionConfig!;
        Assert.NotNull(config.OnPermissionRequest);
        // When AutoApproveTools is false, the handler should NOT be ApproveAll
        Assert.NotEqual(PermissionHandler.ApproveAll, config.OnPermissionRequest);
    }

    [Fact]
    public async Task ProcessAsync_NoModel_ConfigModelIsNull()
    {
        var options = CreateOptions(model: null);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        var config = copilotFactory.LastCreatedClient!.LastSessionConfig!;
        Assert.Null(config.Model);
    }

    // 39.2 Tests — Task prompt construction

    [Fact]
    public void BuildTaskPrompt_IncludesAllRequiredFields()
    {
        var detail = new AgentTaskDetail
        {
            Id = 42,
            Title = "Fix login bug",
            Description = "Users cannot log in after password reset",
            Status = "ToDo",
            Priority = "High"
        };

        var prompt = TaskProcessor.BuildTaskPrompt(detail, "TeamWare");

        Assert.Contains("Task ID: 42", prompt);
        Assert.Contains("Title: Fix login bug", prompt);
        Assert.Contains("Project: TeamWare", prompt);
        Assert.Contains("Priority: High", prompt);
        Assert.Contains("Status: ToDo", prompt);
        Assert.Contains("Users cannot log in after password reset", prompt);
    }

    [Fact]
    public void BuildTaskPrompt_IncludesComments()
    {
        var detail = new AgentTaskDetail
        {
            Id = 42,
            Title = "Fix login bug",
            Status = "ToDo",
            Priority = "Medium",
            Comments =
            [
                new AgentTaskComment
                {
                    Id = 1,
                    AuthorName = "Alice",
                    Content = "This started after the latest deploy",
                    CreatedAt = "2025-01-15T10:00:00Z"
                },
                new AgentTaskComment
                {
                    Id = 2,
                    AuthorName = "Bob",
                    Content = "I can reproduce it consistently",
                    CreatedAt = "2025-01-15T11:00:00Z"
                }
            ]
        };

        var prompt = TaskProcessor.BuildTaskPrompt(detail, "TestProject");

        Assert.Contains("Existing Comments:", prompt);
        Assert.Contains("Alice", prompt);
        Assert.Contains("This started after the latest deploy", prompt);
        Assert.Contains("Bob", prompt);
        Assert.Contains("I can reproduce it consistently", prompt);
    }

    [Fact]
    public void BuildTaskPrompt_NoDescription_OmitsDescriptionSection()
    {
        var detail = new AgentTaskDetail
        {
            Id = 42,
            Title = "Fix it",
            Status = "ToDo",
            Priority = "Low"
        };

        var prompt = TaskProcessor.BuildTaskPrompt(detail, "TestProject");

        Assert.DoesNotContain("Description:", prompt);
    }

    [Fact]
    public void BuildTaskPrompt_NoComments_OmitsCommentsSection()
    {
        var detail = new AgentTaskDetail
        {
            Id = 42,
            Title = "Fix it",
            Status = "ToDo",
            Priority = "Low"
        };

        var prompt = TaskProcessor.BuildTaskPrompt(detail, "TestProject");

        Assert.DoesNotContain("Existing Comments:", prompt);
    }

    [Fact]
    public void BuildTaskPrompt_NullProjectName_ShowsUnknown()
    {
        var detail = new AgentTaskDetail
        {
            Id = 42,
            Title = "Fix it",
            Status = "ToDo",
            Priority = "Low"
        };

        var prompt = TaskProcessor.BuildTaskPrompt(detail, null);

        Assert.Contains("Project: Unknown", prompt);
    }

    // 39.4 Tests — Copilot SDK error handling

    [Fact]
    public async Task ProcessAsync_ClientStartFails_ThrowsException()
    {
        var options = CreateOptions();
        var throwingFactory = new ThrowOnStartCopilotFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, throwingFactory, mcpClient, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(CreateTask(), CancellationToken.None));
    }

    [Fact]
    public async Task ProcessAsync_SessionCreationFails_ThrowsException()
    {
        var options = CreateOptions();
        var throwingFactory = new ThrowOnCreateSessionCopilotFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, throwingFactory, mcpClient, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(CreateTask(), CancellationToken.None));
    }

    [Fact]
    public async Task ProcessAsync_SendAndWaitFails_ThrowsException()
    {
        var options = CreateOptions();
        var throwingFactory = new ThrowOnSendCopilotFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, throwingFactory, mcpClient, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(CreateTask(), CancellationToken.None));
    }

    [Fact]
    public async Task ProcessAsync_SendAndWaitFails_StillDisposesClientAndSession()
    {
        var options = CreateOptions();
        var factory = new ThrowOnSendCopilotFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, factory, mcpClient, logger);

        try
        {
            await processor.ProcessAsync(CreateTask(), CancellationToken.None);
        }
        catch
        {
            // Expected
        }

        Assert.True(factory.LastClient!.DisposeCalled);
        Assert.True(factory.LastSession!.DisposeCalled);
    }

    // Helper factories for error testing

    private class ThrowOnStartCopilotFactory : ICopilotClientWrapperFactory
    {
        public ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger)
        {
            return new FakeCopilotClientWrapper { ThrowOnStart = true };
        }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, string workingDirectory, ILogger logger)
            => Create(options, logger);
    }

    private class ThrowOnCreateSessionCopilotFactory : ICopilotClientWrapperFactory
    {
        public ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger)
        {
            return new FakeCopilotClientWrapper { ThrowOnCreateSession = true };
        }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, string workingDirectory, ILogger logger)
            => Create(options, logger);
    }

    private class ThrowOnSendCopilotFactory : ICopilotClientWrapperFactory
    {
        public FakeCopilotClientWrapper? LastClient { get; private set; }
        public FakeCopilotSessionWrapper? LastSession { get; private set; }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, ILogger logger)
        {
            var session = new FakeCopilotSessionWrapper { ThrowOnSendAndWait = true };
            var client = new ThrowOnSendClient(session);
            LastClient = client.InnerFake;
            LastSession = session;
            return client;
        }

        public ICopilotClientWrapper Create(AgentIdentityOptions options, string workingDirectory, ILogger logger)
            => Create(options, logger);

        private class ThrowOnSendClient : ICopilotClientWrapper
        {
            private readonly FakeCopilotSessionWrapper _session;
            public FakeCopilotClientWrapper InnerFake { get; }
            public bool DisposeCalled { get; private set; }

            public ThrowOnSendClient(FakeCopilotSessionWrapper session)
            {
                _session = session;
                InnerFake = new FakeCopilotClientWrapper();
            }

            public Task StartAsync() => Task.CompletedTask;

            public Task<ICopilotSessionWrapper> CreateSessionAsync(SessionConfig config)
            {
                return Task.FromResult<ICopilotSessionWrapper>(_session);
            }

            public ValueTask DisposeAsync()
            {
                DisposeCalled = true;
                InnerFake.DisposeAsync();
                return ValueTask.CompletedTask;
            }
        }
    }

    // 41.1 Tests — Dry Run Mode

    [Fact]
    public void CreatePermissionHandler_DryRun_ReturnsDryRunHandler()
    {
        var options = CreateOptions(dryRun: true, autoApproveTools: true);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        var handler = processor.CreatePermissionHandler();

        // DryRun takes priority over AutoApproveTools
        Assert.NotEqual(PermissionHandler.ApproveAll, handler);
    }

    [Fact]
    public void CreatePermissionHandler_DryRunFalse_AutoApproveTrue_ReturnsApproveAll()
    {
        var options = CreateOptions(dryRun: false, autoApproveTools: true);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        var handler = processor.CreatePermissionHandler();

        Assert.Equal(PermissionHandler.ApproveAll, handler);
    }

    [Fact]
    public void CreatePermissionHandler_DryRunFalse_AutoApproveFalse_ReturnsCustomHandler()
    {
        var options = CreateOptions(dryRun: false, autoApproveTools: false);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        var handler = processor.CreatePermissionHandler();

        Assert.NotEqual(PermissionHandler.ApproveAll, handler);
    }

    [Fact]
    public async Task CreatePermissionHandler_DryRun_DeniesShellCommands()
    {
        var options = CreateOptions(dryRun: true);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        var handler = processor.CreatePermissionHandler();

        var result = await handler(PermissionRequestFactory.Shell(fullCommandText: "dotnet build"), default!);
        Assert.Equal(PermissionRequestResultKind.DeniedByRules, result.Kind);
    }

    [Fact]
    public async Task CreatePermissionHandler_DryRun_AllowsReadOnlyMcpTools()
    {
        var options = CreateOptions(dryRun: true);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        var handler = processor.CreatePermissionHandler();

        var result = await handler(PermissionRequestFactory.Mcp(toolName: "get_task", serverName: "tw", readOnly: true), default!);
        Assert.Equal(PermissionRequestResultKind.Approved, result.Kind);
    }

    [Fact]
    public async Task CreatePermissionHandler_DryRun_DeniesWriteMcpTools()
    {
        var options = CreateOptions(dryRun: true);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        var handler = processor.CreatePermissionHandler();

        var result = await handler(PermissionRequestFactory.Mcp(toolName: "update_task", serverName: "tw", readOnly: false), default!);
        Assert.Equal(PermissionRequestResultKind.DeniedByRules, result.Kind);
    }

    [Fact]
    public void CreatePermissionHandler_DryRun_LogsDryRunModeEnabled()
    {
        var options = CreateOptions(dryRun: true);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        processor.CreatePermissionHandler();

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Dry run mode enabled"));
    }

    [Fact]
    public async Task ProcessAsync_DryRun_SessionCreatedWithDryRunHandler()
    {
        var options = CreateOptions(dryRun: true);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        var config = copilotFactory.LastCreatedClient!.LastSessionConfig!;
        Assert.NotNull(config.OnPermissionRequest);
        Assert.NotEqual(PermissionHandler.ApproveAll, config.OnPermissionRequest);
    }

    // 41.1 — Dry run mode is configurable per identity (CA-123)

    [Fact]
    public void CreatePermissionHandler_DryRunTrue_IndependentOfAutoApprove()
    {
        // DryRun=true, AutoApproveTools=false — DryRun should still take priority
        var options = CreateOptions(dryRun: true, autoApproveTools: false);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        var handler = processor.CreatePermissionHandler();

        Assert.NotEqual(PermissionHandler.ApproveAll, handler);
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Dry run mode enabled"));
    }

    // 41.3 Tests — Action Restriction Verification

    [Fact]
    public void DefaultSystemPrompt_ContainsNoDoneRule()
    {
        Assert.Contains("Never set a task to Done", DefaultSystemPrompt.Text);
    }

    [Fact]
    public void DefaultSystemPrompt_ContainsNoCreateDeleteRule()
    {
        Assert.Contains("Never create or delete tasks", DefaultSystemPrompt.Text);
    }

    [Fact]
    public void DefaultSystemPrompt_ContainsNoReassignRule()
    {
        Assert.Contains("Never reassign tasks", DefaultSystemPrompt.Text);
    }

    [Fact]
    public void DefaultSystemPrompt_ContainsNoDeleteCommentsRule()
    {
        Assert.Contains("Never delete comments", DefaultSystemPrompt.Text);
    }

    [Fact]
    public void DefaultSystemPrompt_ContainsCommentBeforeStatusRule()
    {
        Assert.Contains("Always post a comment before changing task status", DefaultSystemPrompt.Text);
    }

    [Fact]
    public void DefaultSystemPrompt_ContainsFeatureBranchRule()
    {
        Assert.Contains("feature branch", DefaultSystemPrompt.Text);
        Assert.Contains("agent/", DefaultSystemPrompt.Text);
    }

    [Fact]
    public void DefaultSystemPrompt_ContainsBlockedRule()
    {
        Assert.Contains("Blocked", DefaultSystemPrompt.Text);
    }

    [Fact]
    public async Task ProcessAsync_DefaultSystemPrompt_IncludedInSession()
    {
        var options = CreateOptions(systemPrompt: null);
        var copilotFactory = new FakeCopilotClientWrapperFactory();
        var mcpClient = new FakeMcpClient();
        var logger = new TestLogger<TaskProcessor>();

        var processor = new TaskProcessor(options, copilotFactory, mcpClient, logger);
        await processor.ProcessAsync(CreateTask(), CancellationToken.None);

        var config = copilotFactory.LastCreatedClient!.LastSessionConfig!;
        Assert.Equal(SystemMessageMode.Append, config.SystemMessage!.Mode);
        Assert.Equal(DefaultSystemPrompt.Text, config.SystemMessage.Content);

        // Verify the prompt contains all action restrictions
        var prompt = config.SystemMessage.Content!;
        Assert.Contains("Never set a task to Done", prompt);
        Assert.Contains("Never create or delete tasks", prompt);
        Assert.Contains("Never reassign tasks", prompt);
        Assert.Contains("Never delete comments", prompt);
    }
}
