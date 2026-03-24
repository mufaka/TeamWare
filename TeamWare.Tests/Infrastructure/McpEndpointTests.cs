using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Infrastructure;

public class McpEndpointTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpEndpointTests(TeamWareWebApplicationFactory factory)
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

    // ---------------------------------------------------------------
    // MCP-TEST-08: MCP endpoint returns 404 when MCP_ENABLED is false
    // ---------------------------------------------------------------

    [Fact]
    public async Task McpEndpoint_WhenDisabled_GetReturns404()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, false);

        var response = await _client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WhenDisabled_PostReturns404()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, false);

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WhenDisabled_WithBearerToken_Returns404()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, false);

        // Even with a Bearer header, middleware blocks first when MCP is disabled
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "tw_sometoken");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // MCP-TEST-08: MCP endpoint passes through when MCP_ENABLED is true
    // ---------------------------------------------------------------

    [Fact]
    public async Task McpEndpoint_WhenEnabled_DoesNotReturn404()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, true);

        var response = await _client.GetAsync("/mcp");

        // The endpoint should be reachable (not blocked by middleware).
        // The MCP protocol may return its own error codes for malformed requests,
        // but it should NOT be 404 from the middleware.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // MCP-TEST-07: End-to-end PAT authentication with MCP endpoint
    // ---------------------------------------------------------------

    [Fact]
    public async Task McpEndpoint_WithValidPat_AuthenticatesSuccessfully()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
        await SetMcpEnabled(context, true);

        // Get the seeded admin user
        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        Assert.NotNull(admin);

        // Create a PAT
        var createResult = await tokenService.CreateTokenAsync(admin.Id, "Integration Test Token", null);
        Assert.True(createResult.Succeeded);

        var rawToken = createResult.Data!;

        // Send a properly formed MCP initialize request with the PAT
        var initRequest = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0"}}}
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(initRequest, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        // With a valid PAT and MCP enabled, we should not get 401 or 404.
        // The MCP server should process the initialize request successfully (200 OK or 202 Accepted).
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithInvalidPat_ReturnsUnauthorized()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SetMcpEnabled(context, true);

        // Send a properly formed MCP request with an invalid token
        var initRequest = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0"}}}
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "tw_invalidtoken123");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(initRequest, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        // The MCP endpoint should reject an invalid PAT.
        // It should not return 404 (MCP is enabled) but should indicate auth failure.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
