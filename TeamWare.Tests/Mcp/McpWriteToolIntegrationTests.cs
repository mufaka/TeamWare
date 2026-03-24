using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class McpWriteToolIntegrationTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpWriteToolIntegrationTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private async Task SetMcpEnabled(ApplicationDbContext context, bool enabled)
    {
        await context.Database.EnsureCreatedAsync();
        await SeedData.InitializeAsync(
            _factory.Services.CreateScope().ServiceProvider);

        var config = await context.GlobalConfigurations
            .FirstAsync(gc => gc.Key == "MCP_ENABLED");
        config.Value = enabled ? "true" : "false";
        await context.SaveChangesAsync();
    }

    private HttpRequestMessage CreateMcpToolRequest(string rawToken, string toolName, object? arguments = null)
    {
        var args = arguments ?? new { };
        var requestBody = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = args
            }
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return request;
    }

    // ---------------------------------------------------------------
    // MCP-36: All write tools require PAT authentication
    // ---------------------------------------------------------------

    [Fact]
    public async Task CreateTask_WithoutAuth_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, true);

        var requestBody = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"create_task","arguments":{"projectId":1,"title":"Test"}}}
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CaptureInbox_WithoutAuth_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, true);

        var requestBody = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"capture_inbox","arguments":{"title":"Test"}}}
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTaskStatus_WithInvalidPat_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, true);

        var request = CreateMcpToolRequest("tw_invalidtoken123456", "update_task_status", new { taskId = 1, status = "Done" });

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AssignTask_WithInvalidPat_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, true);

        var request = CreateMcpToolRequest("tw_invalidtoken123456", "assign_task", new { taskId = 1, userIds = new[] { "user1" } });

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddComment_WithInvalidPat_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, true);

        var request = CreateMcpToolRequest("tw_invalidtoken123456", "add_comment", new { taskId = 1, content = "Test" });

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProcessInboxItem_WithInvalidPat_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, true);

        var request = CreateMcpToolRequest("tw_invalidtoken123456", "process_inbox_item", new { inboxItemId = 1, projectId = 1, priority = "Medium" });

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // MCP-37: Authorization enforcement matches web UI behavior
    // ---------------------------------------------------------------

    // Note: Authorization enforcement is tested at the unit level in individual tool tests
    // (e.g., CreateTask_NonMember_ReturnsError, UpdateTaskStatus_NonMember_ReturnsError,
    // AssignTask_NonMember_ReturnsError). These exercise the same service-layer authorization
    // that the web UI uses.

    // ---------------------------------------------------------------
    // MCP-SEC-06: Input validation rejects oversized strings
    // ---------------------------------------------------------------

    // Note: Input validation is tested at the unit level in individual tool tests
    // (e.g., CreateTask_TitleTooLong_ReturnsError, CreateTask_DescriptionTooLong_ReturnsError,
    // AddComment_ContentTooLong_ReturnsError, CaptureInbox_TitleTooLong_ReturnsError,
    // CaptureInbox_DescriptionTooLong_ReturnsError).

    // ---------------------------------------------------------------
    // MCP-38, MCP-74: Tools return descriptive error messages on failure
    // ---------------------------------------------------------------

    // Note: Descriptive error messages and ServiceResult failure propagation are tested at
    // the unit level in individual tool tests (e.g., NonExistent and NonMember scenarios).
}
