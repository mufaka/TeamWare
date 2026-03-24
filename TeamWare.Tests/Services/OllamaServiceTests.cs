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

public class OllamaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly GlobalConfigurationService _configService;
    private readonly IMemoryCache _cache;

    public OllamaServiceTests()
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

        var activityLogService = new AdminActivityLogService(_context);
        _configService = new GlobalConfigurationService(_context, activityLogService);
        _cache = new MemoryCache(new MemoryCacheOptions());
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

    private OllamaService CreateService(HttpResponseMessage? response = null, Exception? exception = null)
    {
        var handler = new FakeHttpMessageHandler(response, exception);
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        return new OllamaService(_configService, factory, _cache);
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

    [Fact]
    public async Task IsConfigured_ReturnsFalse_WhenUrlNotSeeded()
    {
        var service = CreateService();

        Assert.False(await service.IsConfigured());
    }

    [Fact]
    public async Task IsConfigured_ReturnsFalse_WhenUrlIsEmpty()
    {
        await SeedConfigAsync("OLLAMA_URL", "");
        var service = CreateService();

        Assert.False(await service.IsConfigured());
    }

    [Fact]
    public async Task IsConfigured_ReturnsTrue_WhenUrlHasValue()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var service = CreateService();

        Assert.True(await service.IsConfigured());
    }

    [Fact]
    public async Task GenerateCompletion_ReturnsFailure_WhenNotConfigured()
    {
        await SeedConfigAsync("OLLAMA_URL", "");
        var service = CreateService();

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Contains("not configured", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateCompletion_ReturnsSuccess_WhenConfiguredAndOllamaResponds()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        await SeedConfigAsync("OLLAMA_MODEL", "llama3.1");
        var response = CreateOllamaResponse("Rewritten text here.");
        var service = CreateService(response);

        var result = await service.GenerateCompletion("You are a helpful assistant.", "Rewrite this.");

        Assert.True(result.Succeeded);
        Assert.Equal("Rewritten text here.", result.Data);
    }

    [Fact]
    public async Task GenerateCompletion_UsesDefaultModel_WhenModelIsEmpty()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        await SeedConfigAsync("OLLAMA_MODEL", "");
        var response = CreateOllamaResponse("Result text.");
        var service = CreateService(response);

        var result = await service.GenerateCompletion("system", "user");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GenerateCompletion_ReturnsFailure_OnTimeout()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        await SeedConfigAsync("OLLAMA_TIMEOUT", "1");
        var service = CreateService(exception: new TaskCanceledException());

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateCompletion_ReturnsFailure_OnHttpError()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var service = CreateService(exception: new HttpRequestException());

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Contains("unavailable", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateCompletion_ReturnsFailure_OnNonSuccessStatusCode()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var service = CreateService(response);

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Contains("unavailable", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateCompletion_ReturnsFailure_OnEmptyResponse()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var response = CreateOllamaResponse("");
        var service = CreateService(response);

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Contains("Could not generate", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateCompletion_ReturnsFailure_OnUnparseableResponse()
    {
        await SeedConfigAsync("OLLAMA_URL", "http://localhost:11434");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", Encoding.UTF8, "application/json")
        };
        var service = CreateService(response);

        var result = await service.GenerateCompletion("system", "user");

        Assert.False(result.Succeeded);
        Assert.Contains("Could not generate", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _cache.Dispose();
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
