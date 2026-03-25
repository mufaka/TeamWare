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

public class McpPromptResourceIntegrationTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpPromptResourceIntegrationTests(TeamWareWebApplicationFactory factory)
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

    private HttpRequestMessage CreateMcpPromptRequest(string rawToken, string promptName, object? arguments = null)
    {
        var args = arguments ?? new { };
        var requestBody = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "prompts/get",
            @params = new
            {
                name = promptName,
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

    private HttpRequestMessage CreateMcpResourceRequest(string rawToken, string resourceUri)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "resources/read",
            @params = new
            {
                uri = resourceUri
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
    // MCP-43: All prompts require PAT authentication
    // ---------------------------------------------------------------

    [Fact]
    public async Task PromptCall_WithoutAuth_IsRejected()
    {
        await EnsureSeeded();

        var requestBody = """
            {"jsonrpc":"2.0","id":1,"method":"prompts/get","params":{"name":"standup","arguments":{}}}
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PromptCall_WithInvalidPat_IsRejected()
    {
        await EnsureSeeded();

        var request = CreateMcpPromptRequest("tw_invalidtoken123456", "standup");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // MCP-52: All resources require PAT authentication
    // ---------------------------------------------------------------

    [Fact]
    public async Task ResourceRead_WithoutAuth_IsRejected()
    {
        await EnsureSeeded();

        var requestBody = """
            {"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"teamware://dashboard"}}
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResourceRead_WithInvalidPat_IsRejected()
    {
        await EnsureSeeded();

        var request = CreateMcpResourceRequest("tw_invalidtoken123456", "teamware://dashboard");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // MCP-44: Prompts enforce project membership
    // ---------------------------------------------------------------

    [Fact]
    public async Task ProjectContextPrompt_NonMember_ReturnsError()
    {
        await EnsureSeeded();

        using var scope = _factory.Services.CreateScope();

        // Create a user and project that the PAT user is NOT a member of
        var otherUser = new ApplicationUser
        {
            UserName = "other@test.com",
            Email = "other@test.com",
            DisplayName = "Other User"
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await userManager.CreateAsync(otherUser, "Test123!");

        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var projectResult = await projectService.CreateProject("Private Project", null, otherUser.Id);
        Assert.True(projectResult.Succeeded);

        var (user, rawToken) = await CreateUserWithPat();

        // First initialize the MCP session
        var initRequest = CreateMcpInitializeRequest(rawToken);
        var initResponse = await _client.SendAsync(initRequest);

        // Then call the prompt with a project the PAT user is not a member of
        var request = CreateMcpPromptRequest(rawToken, "project_context", new { projectId = projectResult.Data!.Id });
        var response = await _client.SendAsync(request);

        // The response should either be an error status or contain an error in the MCP response
        var responseBody = await response.Content.ReadAsStringAsync();
        // MCP protocol may return 200 with an error in the JSON-RPC response body
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Contains("Error", responseBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------
    // MCP-53: Resources enforce project membership
    // ---------------------------------------------------------------

    [Fact]
    public async Task ProjectSummaryResource_NonMember_ReturnsError()
    {
        await EnsureSeeded();

        using var scope = _factory.Services.CreateScope();

        var otherUser = new ApplicationUser
        {
            UserName = "other2@test.com",
            Email = "other2@test.com",
            DisplayName = "Other User 2"
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await userManager.CreateAsync(otherUser, "Test123!");

        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var projectResult = await projectService.CreateProject("Private Project 2", null, otherUser.Id);
        Assert.True(projectResult.Succeeded);

        var (user, rawToken) = await CreateUserWithPat();

        var initRequest = CreateMcpInitializeRequest(rawToken);
        await _client.SendAsync(initRequest);

        var request = CreateMcpResourceRequest(rawToken, $"teamware://projects/{projectResult.Data!.Id}/summary");
        var response = await _client.SendAsync(request);

        var responseBody = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.OK)
        {
            // Should contain an error indication
            Assert.True(
                responseBody.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                responseBody.Contains("Error", StringComparison.OrdinalIgnoreCase));
        }
    }
}
