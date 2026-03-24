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

        return new AiAssistantService(ollamaService);
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

        return new AiAssistantService(ollamaService);
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
    public async Task GenerateProjectSummary_ReturnsStubFailure()
    {
        var service = CreateService();
        var result = await service.GenerateProjectSummary(1, "user-id", SummaryPeriod.ThisWeek);

        Assert.False(result.Succeeded);
        Assert.Contains("not yet implemented", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeneratePersonalDigest_ReturnsStubFailure()
    {
        var service = CreateService();
        var result = await service.GeneratePersonalDigest("user-id");

        Assert.False(result.Succeeded);
        Assert.Contains("not yet implemented", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateReviewPreparation_ReturnsStubFailure()
    {
        var service = CreateService();
        var result = await service.GenerateReviewPreparation("user-id");

        Assert.False(result.Succeeded);
        Assert.Contains("not yet implemented", result.Errors[0], StringComparison.OrdinalIgnoreCase);
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
