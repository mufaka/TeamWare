using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class AiAssistantServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly GlobalConfigurationService _configService;
    private readonly string _testUserId;

    public AiAssistantServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        var adminUser = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            DisplayName = "Admin User"
        };
        _context.Users.Add(adminUser);
        _context.SaveChanges();

        _testUserId = adminUser.Id;

        _cache = new MemoryCache(new MemoryCacheOptions());
        var activityLogService = new AdminActivityLogService(_context);
        _configService = new GlobalConfigurationService(_context, activityLogService, _cache);
    }

    private AiAssistantService CreateService(string ollamaResponse = "AI generated text.")
    {
        // Seed Ollama as configured
        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = "OLLAMA_URL",
            Value = "http://localhost:11434",
            UpdatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();

        var responseBody = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = ollamaResponse }
        });

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        var ollamaService = new OllamaService(_configService, factory, _cache);

        return CreateAiAssistantService(ollamaService);
    }

    private AiAssistantService CreateUnconfiguredService()
    {
        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = "OLLAMA_URL",
            Value = "",
            UpdatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();

        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        var ollamaService = new OllamaService(_configService, factory, _cache);

        return CreateAiAssistantService(ollamaService);
    }

    private AiAssistantService CreateAiAssistantService(OllamaService ollamaService)
    {
        var activityLogService = new ActivityLogService(_context);
        var notificationService = new NotificationService(_context);
        var taskService = new TaskService(_context, activityLogService, notificationService);
        var inboxService = new InboxService(_context, taskService, notificationService);
        var progressService = new ProgressService(_context);
        var projectService = new ProjectService(_context);

        return new AiAssistantService(
            ollamaService,
            activityLogService,
            progressService,
            taskService,
            inboxService,
            projectService);
    }

    private int CreateProjectWithMember(string userId)
    {
        var project = new Project
        {
            Name = "Test Project",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Projects.Add(project);
        _context.SaveChanges();

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        });
        _context.SaveChanges();

        return project.Id;
    }

    [Fact]
    public async Task IsAvailable_ReturnsTrue_WhenOllamaConfigured()
    {
        var service = CreateService();
        Assert.True(await service.IsAvailable());
    }

    [Fact]
    public async Task IsAvailable_ReturnsFalse_WhenOllamaNotConfigured()
    {
        var service = CreateUnconfiguredService();
        Assert.False(await service.IsAvailable());
    }

    [Fact]
    public async Task RewriteProjectDescription_ReturnsSuccess()
    {
        var service = CreateService("Polished project description.");
        var result = await service.RewriteProjectDescription("Draft project text.");

        Assert.True(result.Succeeded);
        Assert.Equal("Polished project description.", result.Data);
    }

    [Fact]
    public async Task RewriteProjectDescription_PropagatesFailure_WhenNotConfigured()
    {
        var service = CreateUnconfiguredService();
        var result = await service.RewriteProjectDescription("Draft text.");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RewriteTaskDescription_ReturnsSuccess()
    {
        var service = CreateService("Clear task requirement.");
        var result = await service.RewriteTaskDescription("Fix the login.");

        Assert.True(result.Succeeded);
        Assert.Equal("Clear task requirement.", result.Data);
    }

    [Fact]
    public async Task RewriteTaskDescription_PropagatesFailure_WhenNotConfigured()
    {
        var service = CreateUnconfiguredService();
        var result = await service.RewriteTaskDescription("Draft text.");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PolishComment_ReturnsSuccess()
    {
        var service = CreateService("Well-written comment.");
        var result = await service.PolishComment("rough comment");

        Assert.True(result.Succeeded);
        Assert.Equal("Well-written comment.", result.Data);
    }

    [Fact]
    public async Task PolishComment_PropagatesFailure_WhenNotConfigured()
    {
        var service = CreateUnconfiguredService();
        var result = await service.PolishComment("rough comment");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExpandInboxItem_ReturnsSuccess_WithTitleOnly()
    {
        var service = CreateService("Expanded description.");
        var result = await service.ExpandInboxItem("Fix login bug", null);

        Assert.True(result.Succeeded);
        Assert.Equal("Expanded description.", result.Data);
    }

    [Fact]
    public async Task ExpandInboxItem_ReturnsSuccess_WithTitleAndDescription()
    {
        var service = CreateService("Expanded description with context.");
        var result = await service.ExpandInboxItem("Fix login bug", "Mobile only");

        Assert.True(result.Succeeded);
        Assert.Equal("Expanded description with context.", result.Data);
    }

    [Fact]
    public async Task ExpandInboxItem_PropagatesFailure_WhenNotConfigured()
    {
        var service = CreateUnconfiguredService();
        var result = await service.ExpandInboxItem("Fix login bug", null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GenerateProjectSummary_ReturnsSuccess_WithActivityData()
    {
        var service = CreateService("Project is on track with 2 tasks completed.");
        var projectId = CreateProjectWithMember(_testUserId);

        // Add a task and activity
        var task = new TaskItem
        {
            Title = "Test Task",
            ProjectId = projectId,
            CreatedByUserId = _testUserId,
            Status = TaskItemStatus.Done,
            Priority = TaskItemPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.TaskItems.Add(task);
        _context.SaveChanges();

        _context.ActivityLogEntries.Add(new ActivityLogEntry
        {
            TaskItemId = task.Id,
            ProjectId = projectId,
            UserId = _testUserId,
            ChangeType = ActivityChangeType.StatusChanged,
            NewValue = "Done",
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();

        var result = await service.GenerateProjectSummary(projectId, _testUserId, SummaryPeriod.ThisWeek);

        Assert.True(result.Succeeded);
        Assert.Equal("Project is on track with 2 tasks completed.", result.Data);
    }

    [Fact]
    public async Task GenerateProjectSummary_PropagatesFailure_WhenNotConfigured()
    {
        var service = CreateUnconfiguredService();
        var result = await service.GenerateProjectSummary(1, _testUserId, SummaryPeriod.ThisWeek);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GeneratePersonalDigest_ReturnsSuccess_WithUserData()
    {
        var service = CreateService("You completed 3 tasks today.");
        var projectId = CreateProjectWithMember(_testUserId);

        var task = new TaskItem
        {
            Title = "Completed Task",
            ProjectId = projectId,
            CreatedByUserId = _testUserId,
            Status = TaskItemStatus.Done,
            IsNextAction = true,
            Priority = TaskItemPriority.High,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.TaskItems.Add(task);
        _context.SaveChanges();

        _context.ActivityLogEntries.Add(new ActivityLogEntry
        {
            TaskItemId = task.Id,
            ProjectId = projectId,
            UserId = _testUserId,
            ChangeType = ActivityChangeType.StatusChanged,
            NewValue = "Done",
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();

        var result = await service.GeneratePersonalDigest(_testUserId);

        Assert.True(result.Succeeded);
        Assert.Equal("You completed 3 tasks today.", result.Data);
    }

    [Fact]
    public async Task GeneratePersonalDigest_PropagatesFailure_WhenNotConfigured()
    {
        var service = CreateUnconfiguredService();
        var result = await service.GeneratePersonalDigest(_testUserId);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GenerateReviewPreparation_ReturnsSuccess_WithReviewData()
    {
        var service = CreateService("You have 2 inbox items and 1 overdue task.");
        var projectId = CreateProjectWithMember(_testUserId);

        _context.InboxItems.Add(new InboxItem
        {
            Title = "Unprocessed idea",
            UserId = _testUserId,
            Status = InboxItemStatus.Unprocessed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _context.TaskItems.Add(new TaskItem
        {
            Title = "Someday task",
            ProjectId = projectId,
            CreatedByUserId = _testUserId,
            Status = TaskItemStatus.ToDo,
            IsSomedayMaybe = true,
            Priority = TaskItemPriority.Low,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();

        var result = await service.GenerateReviewPreparation(_testUserId);

        Assert.True(result.Succeeded);
        Assert.Equal("You have 2 inbox items and 1 overdue task.", result.Data);
    }

    [Fact]
    public async Task GenerateReviewPreparation_PropagatesFailure_WhenNotConfigured()
    {
        var service = CreateUnconfiguredService();
        var result = await service.GenerateReviewPreparation(_testUserId);

        Assert.False(result.Succeeded);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FakeHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }
}
