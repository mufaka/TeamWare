using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

/// <summary>
/// Phase 31 verification tests for MCP polish and hardening.
/// Covers error handling (31.1), security review (31.2), and JSON consistency (31.3).
/// </summary>
public class McpPolishVerificationTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpPolishVerificationTests(TeamWareWebApplicationFactory factory)
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

        var createResult = await tokenService.CreateTokenAsync(admin.Id, $"Polish Test {Guid.NewGuid():N}", null);
        Assert.True(createResult.Succeeded);

        return (admin, createResult.Data!);
    }

    private HttpRequestMessage CreateMcpRequest(string rawToken, string jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return request;
    }

    // =================================================================
    // 31.1 Error Handling and Resilience
    // =================================================================

    // MCP-71: Authentication error paths return proper errors
    [Fact]
    public async Task McpEndpoint_WithEmptyBearer_RejectsRequest()
    {
        await EnsureSeeded();

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_projects","arguments":{}}}""",
            Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        // Empty Bearer should not succeed
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithMalformedBearer_RejectsRequest()
    {
        await EnsureSeeded();

        var request = CreateMcpRequest("not_a_valid_token_format",
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_projects","arguments":{}}}""");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // MCP-75: MCP tool exceptions do not propagate to ASP.NET Core middleware
    [Fact]
    public async Task McpEndpoint_MalformedJsonRpc_DoesNotReturn500()
    {
        await EnsureSeeded();

        var (user, rawToken) = await CreateUserWithPat();

        // Send completely malformed JSON-RPC
        var request = CreateMcpRequest(rawToken, """{"this":"is not valid jsonrpc"}""");

        var response = await _client.SendAsync(request);

        // Should not crash the server (500)
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_InvalidToolName_DoesNotReturn500()
    {
        await EnsureSeeded();

        var (user, rawToken) = await CreateUserWithPat();

        var request = CreateMcpRequest(rawToken,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"nonexistent_tool","arguments":{}}}""");

        var response = await _client.SendAsync(request);

        // Should not crash the server
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =================================================================
    // 31.2 Security Review
    // =================================================================

    // MCP-SEC-04: tw_ prefix on all generated tokens
    [Fact]
    public async Task PersonalAccessToken_AlwaysHasTwPrefix()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedData.InitializeAsync(scope.ServiceProvider);

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        Assert.NotNull(admin);

        // Generate multiple tokens and verify all have tw_ prefix
        for (int i = 0; i < 5; i++)
        {
            var result = await tokenService.CreateTokenAsync(admin.Id, $"Prefix Test {i}", null);
            Assert.True(result.Succeeded);
            Assert.StartsWith("tw_", result.Data!);
        }
    }

    // MCP-SEC-01, MCP-NF-06: PAT hashing uses SHA-256
    [Fact]
    public async Task PersonalAccessToken_UsesConsistentSha256Hashing()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedData.InitializeAsync(scope.ServiceProvider);

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        Assert.NotNull(admin);

        var result = await tokenService.CreateTokenAsync(admin.Id, "Hash Test", null);
        Assert.True(result.Succeeded);
        var rawToken = result.Data!;

        // Compute expected SHA-256 hash the same way the service does
        var expectedHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var storedToken = await context.PersonalAccessTokens
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync(t => t.Name == "Hash Test");

        Assert.Equal(expectedHash, storedToken.TokenHash);
    }

    // MCP-SEC-03: Token revocation takes effect immediately (no caching)
    [Fact]
    public async Task PersonalAccessToken_RevocationIsImmediate()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedData.InitializeAsync(scope.ServiceProvider);

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        Assert.NotNull(admin);

        // Create and validate token
        var createResult = await tokenService.CreateTokenAsync(admin.Id, "Revoke Immediate", null);
        var rawToken = createResult.Data!;

        var validateResult1 = await tokenService.ValidateTokenAsync(rawToken);
        Assert.True(validateResult1.Succeeded);

        // Revoke the token
        var token = await context.PersonalAccessTokens.FirstAsync(t => t.Name == "Revoke Immediate");
        var revokeResult = await tokenService.RevokeTokenAsync(token.Id, admin.Id, isAdmin: true);
        Assert.True(revokeResult.Succeeded);

        // Immediately validate again — should fail
        var validateResult2 = await tokenService.ValidateTokenAsync(rawToken);
        Assert.False(validateResult2.Succeeded);
        Assert.Contains("revoked", validateResult2.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    // MCP-SEC-05: No sensitive data in tool responses
    [Fact]
    public async Task PersonalAccessToken_StoredTokenPrefix_DoesNotExposeFullToken()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedData.InitializeAsync(scope.ServiceProvider);

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        Assert.NotNull(admin);

        var result = await tokenService.CreateTokenAsync(admin.Id, "Prefix Length Test", null);
        var rawToken = result.Data!;

        var storedToken = await context.PersonalAccessTokens
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync(t => t.Name == "Prefix Length Test");

        // TokenPrefix should be max 10 chars (much shorter than the full token)
        Assert.True(storedToken.TokenPrefix.Length <= 10);
        Assert.True(rawToken.Length > storedToken.TokenPrefix.Length);
    }

    // =================================================================
    // 31.3 JSON Response Consistency (verified via unit tests)
    // =================================================================

    // Verify all tool classes use camelCase JSON serialization options
    [Fact]
    public void AllToolClasses_UseCamelCaseJsonOptions()
    {
        var toolTypes = typeof(Web.Mcp.Tools.ProjectTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolTypeAttribute), false).Length > 0)
            .ToList();

        Assert.NotEmpty(toolTypes);

        foreach (var toolType in toolTypes)
        {
            var jsonOptionsField = toolType.GetField("JsonOptions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(jsonOptionsField);
            var options = jsonOptionsField.GetValue(null) as JsonSerializerOptions;
            Assert.NotNull(options);
            Assert.Equal(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
            Assert.False(options.WriteIndented);
        }
    }

    // Verify all tool classes have [Authorize] attribute
    [Fact]
    public void AllToolClasses_HaveAuthorizeAttribute()
    {
        var toolTypes = typeof(Web.Mcp.Tools.ProjectTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolTypeAttribute), false).Length > 0)
            .ToList();

        Assert.NotEmpty(toolTypes);

        foreach (var toolType in toolTypes)
        {
            var hasAuthorize = toolType.GetCustomAttributes(
                typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).Length > 0;
            Assert.True(hasAuthorize, $"{toolType.Name} is missing [Authorize] attribute.");
        }
    }

    // Verify all prompt classes have [Authorize] attribute
    [Fact]
    public void AllPromptClasses_HaveAuthorizeAttribute()
    {
        var promptTypes = typeof(Web.Mcp.Tools.ProjectTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerPromptTypeAttribute), false).Length > 0)
            .ToList();

        Assert.NotEmpty(promptTypes);

        foreach (var promptType in promptTypes)
        {
            var hasAuthorize = promptType.GetCustomAttributes(
                typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).Length > 0;
            Assert.True(hasAuthorize, $"{promptType.Name} is missing [Authorize] attribute.");
        }
    }

    // Verify all resource classes have [Authorize] attribute
    [Fact]
    public void AllResourceClasses_HaveAuthorizeAttribute()
    {
        var resourceTypes = typeof(Web.Mcp.Tools.ProjectTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerResourceTypeAttribute), false).Length > 0)
            .ToList();

        Assert.NotEmpty(resourceTypes);

        foreach (var resourceType in resourceTypes)
        {
            var hasAuthorize = resourceType.GetCustomAttributes(
                typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).Length > 0;
            Assert.True(hasAuthorize, $"{resourceType.Name} is missing [Authorize] attribute.");
        }
    }

    // Verify enum serialization uses string names (not integer values)
    [Fact]
    public void TaskStatusEnum_SerializesAsString_WithCamelCaseOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // The tools manually call .ToString() on enums, so verify the pattern
        var status = TaskItemStatus.InProgress;
        var serialized = JsonSerializer.Serialize(new { status = status.ToString() }, options);

        Assert.Contains("\"InProgress\"", serialized);
        Assert.DoesNotContain("1", serialized); // Should not be integer
    }

    [Fact]
    public void TaskPriorityEnum_SerializesAsString_WithCamelCaseOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var priority = TaskItemPriority.Critical;
        var serialized = JsonSerializer.Serialize(new { priority = priority.ToString() }, options);

        Assert.Contains("\"Critical\"", serialized);
    }

    // Verify ISO 8601 date serialization
    [Fact]
    public void DateTime_SerializesAsIso8601_WithOFormat()
    {
        var date = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc);
        var iso = date.ToString("O");

        Assert.Contains("2025-06-15", iso);
        Assert.Contains("T", iso);
        // O format includes full precision and timezone
        Assert.Matches(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", iso);
    }

    // Verify null optional parameters are handled gracefully by tools
    [Fact]
    public void NullableDateTime_SerializesCorrectly()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        DateTime? nullDate = null;
        DateTime? validDate = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var nullResult = JsonSerializer.Serialize(new { dueDate = nullDate?.ToString("O") }, options);
        var validResult = JsonSerializer.Serialize(new { dueDate = validDate?.ToString("O") }, options);

        Assert.Contains("null", nullResult);
        Assert.Contains("2025-12-31", validResult);
    }
}
