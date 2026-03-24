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

public class AiResilienceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly GlobalConfigurationService _configService;
    private readonly IMemoryCache _cache;

    public AiResilienceTests()
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

        _cache = new MemoryCache(new MemoryCacheOptions());
        var activityLogService = new AdminActivityLogService(_context);
        _configService = new GlobalConfigurationService(_context, activityLogService, _cache);
    }

    private async Task SeedConfigAsync(string key, string value)
    {
        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = key,
            Value = value,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private OllamaService CreateOllamaService(HttpResponseMessage? response = null, Exception? exception = null)
    {
        var handler = new FakeHttpMessageHandler(response, exception);
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        return new OllamaService(_configService, factory, _cache);
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

    private static HttpResponseMessage CreateOllamaResponse(string content)
    {
        var responseBody = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }

    // --- Error paths return user-friendly messages ---

    [Fact]
    public async Task TimeoutError_ReturnsUserFriendlyMessage()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        await SeedConfigAsync("OLLAMA_TIMEOUT", "1");
        var service = CreateOllamaService(exception: new TaskCanceledException());

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
        Assert.Contains("timed out", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpError_ReturnsUserFriendlyMessage()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var service = CreateOllamaService(exception: new HttpRequestException("Connection refused"));

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
        Assert.Contains("unavailable", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonSuccessStatusCode_ReturnsUserFriendlyMessage()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var service = CreateOllamaService(response);

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
        Assert.Contains("unavailable", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyResponse_ReturnsUserFriendlyMessage()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var response = CreateOllamaResponse("");
        var service = CreateOllamaService(response);

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
        Assert.Contains("could not generate", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnparseableResponse_ReturnsUserFriendlyMessage()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not json</html>", Encoding.UTF8, "application/json")
        };
        var service = CreateOllamaService(response);

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task NotConfigured_ReturnsUserFriendlyMessage()
    {
        await SeedConfigAsync("OLLAMA_URL", "");
        var service = CreateOllamaService();

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
        Assert.Contains("not configured", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    // --- AI errors never block primary save actions ---
    // (Verified by design: AI endpoints are separate POST actions, not part of project/task/comment save flows)

    [Fact]
    public async Task AiFailure_DoesNotPropagate_AiAssistantService_RewriteProjectDescription()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var ollamaService = CreateOllamaService(exception: new HttpRequestException("Connection refused"));
        var aiService = CreateAiAssistantService(ollamaService);

        var result = await aiService.RewriteProjectDescription("Test description");

        // The result is a failure but no exception propagates
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task AiFailure_DoesNotPropagate_AiAssistantService_PolishComment()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var ollamaService = CreateOllamaService(exception: new HttpRequestException("Connection refused"));
        var aiService = CreateAiAssistantService(ollamaService);

        var result = await aiService.PolishComment("Test comment");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task AiFailure_DoesNotPropagate_AiAssistantService_RewriteTaskDescription()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var ollamaService = CreateOllamaService(exception: new HttpRequestException("Connection refused"));
        var aiService = CreateAiAssistantService(ollamaService);

        var result = await aiService.RewriteTaskDescription("Test task description");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task AiFailure_DoesNotPropagate_AiAssistantService_ExpandInboxItem()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var ollamaService = CreateOllamaService(exception: new HttpRequestException("Connection refused"));
        var aiService = CreateAiAssistantService(ollamaService);

        var result = await aiService.ExpandInboxItem("Some title", "Some description");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    // --- Config cache responds to changes ---

    [Fact]
    public async Task IsConfigured_BecomesTrue_WhenUrlIsSetAfterStart()
    {
        await SeedConfigAsync("OLLAMA_URL", "");
        var service = CreateOllamaService();

        Assert.False(await service.IsConfigured());

        // Simulate admin updating the URL
        var config = await _context.GlobalConfigurations.FirstAsync(c => c.Key == "OLLAMA_URL");
        config.Value = "http://localhost:11434";
        await _context.SaveChangesAsync();

        // Clear cache to simulate cache expiry
        _cache.Remove("OllamaConfig_OLLAMA_URL");

        Assert.True(await service.IsConfigured());
    }

    [Fact]
    public async Task IsConfigured_BecomesFalse_WhenUrlIsCleared()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var service = CreateOllamaService();

        Assert.True(await service.IsConfigured());

        // Simulate admin clearing the URL
        var config = await _context.GlobalConfigurations.FirstAsync(c => c.Key == "OLLAMA_URL");
        config.Value = "";
        await _context.SaveChangesAsync();

        // Clear cache to simulate cache expiry
        _cache.Remove("OllamaConfig_OLLAMA_URL");

        Assert.False(await service.IsConfigured());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public FakeHttpMessageHandler(HttpResponseMessage? response, Exception? exception)
        {
            _response = response;
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception != null)
            {
                throw _exception;
            }

            return Task.FromResult(_response ?? new HttpResponseMessage(HttpStatusCode.OK));
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
