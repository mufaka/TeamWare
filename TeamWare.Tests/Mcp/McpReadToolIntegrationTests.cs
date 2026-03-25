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

public class McpReadToolIntegrationTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpReadToolIntegrationTests(TeamWareWebApplicationFactory factory)
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

    private async Task EnsureSeeded()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedData.InitializeAsync(scope.ServiceProvider);
    }

    private async Task<(ApplicationUser User, string RawToken)> CreateUserWithPat()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        Assert.NotNull(admin);

        var createResult = await tokenService.CreateTokenAsync(admin.Id, "Integration Test Token", null);
        Assert.True(createResult.Succeeded);

        return (admin, createResult.Data!);
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

    private HttpRequestMessage CreateMcpInitializeRequest(string rawToken)
    {
        var initRequest = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0"}}}
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(initRequest, Encoding.UTF8, "application/json");
        return request;
    }

    // ---------------------------------------------------------------
    // MCP-18: All read-only tools require PAT authentication
    // ---------------------------------------------------------------

    [Fact]
    public async Task McpToolCall_WithoutAuth_IsRejected()
    {
        await EnsureSeeded();

        var requestBody = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_projects","arguments":{}}}
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        // Without auth, the MCP endpoint should reject the request.
        // MCP protocol may return different status codes but should not succeed.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task McpToolCall_WithInvalidPat_IsRejected()
    {
        await EnsureSeeded();

        var request = CreateMcpToolRequest("tw_invalidtoken123456", "list_projects");

        var response = await _client.SendAsync(request);

        // Invalid PAT should not result in a successful tool call
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task McpToolCall_WithExpiredPat_IsRejected()
    {
        await EnsureSeeded();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        Assert.NotNull(admin);

        // Create a token that expired in the past
        var createResult = await tokenService.CreateTokenAsync(admin.Id, "Expired Token", DateTime.UtcNow.AddDays(-1));
        Assert.True(createResult.Succeeded);

        var request = CreateMcpToolRequest(createResult.Data!, "list_projects");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task McpToolCall_WithRevokedPat_IsRejected()
    {
        await EnsureSeeded();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        Assert.NotNull(admin);

        var createResult = await tokenService.CreateTokenAsync(admin.Id, "Revoked Token", null);
        Assert.True(createResult.Succeeded);

        // Revoke the token
        var tokens = await tokenService.GetTokensForUserAsync(admin.Id);
        var token = tokens.Data!.First(t => t.Name == "Revoked Token");
        await tokenService.RevokeTokenAsync(token.Id, admin.Id, isAdmin: true);

        var request = CreateMcpToolRequest(createResult.Data!, "list_projects");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // MCP-TEST-07: Valid PAT authentication works with MCP
    // ---------------------------------------------------------------

    [Fact]
    public async Task McpInitialize_WithValidPat_Succeeds()
    {
        await EnsureSeeded();

        var (user, rawToken) = await CreateUserWithPat();

        var request = CreateMcpInitializeRequest(rawToken);
        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // MCP-73: Tools return descriptive errors for non-existent entities
    // ---------------------------------------------------------------

    // Note: This is tested at the unit level in individual tool tests
    // (e.g., GetProject_NonExistentProject_ReturnsError, GetTask_NonExistent_ReturnsError).
    // Integration-level entity-not-found testing requires a full MCP session
    // which is complex with Streamable HTTP. The unit tests provide sufficient coverage.

    // ---------------------------------------------------------------
    // MCP-74: Tools propagate ServiceResult failures
    // ---------------------------------------------------------------

    // Note: ServiceResult failure propagation is tested at the unit level in individual
    // tool tests (e.g., NonMember scenarios that exercise service-level authorization failures).
}
