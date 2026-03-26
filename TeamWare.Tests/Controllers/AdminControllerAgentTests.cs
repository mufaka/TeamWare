using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Controllers;

public class AdminControllerAgentTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminControllerAgentTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> LoginAsAdmin()
    {
        return await GetLoginCookie(SeedData.AdminEmail, SeedData.AdminPassword);
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email, string displayName)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            return (existing.Id, await GetLoginCookie(email, "TestPass1!"));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };

        await userManager.CreateAsync(user, "TestPass1!");
        await userManager.AddToRoleAsync(user, SeedData.UserRoleName);

        var cookie = await GetLoginCookie(email, "TestPass1!");
        return (user.Id, cookie);
    }

    private async Task<string> GetLoginCookie(string email, string password)
    {
        var getResponse = await _client.GetAsync("/Account/Login");
        var getContent = await getResponse.Content.ReadAsStringAsync();

        var token = ExtractAntiForgeryToken(getContent);
        var cookies = getResponse.Headers.GetValues("Set-Cookie");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token
        });

        var loginResponse = await _client.SendAsync(request);
        var loginCookies = loginResponse.Headers.GetValues("Set-Cookie");

        return string.Join("; ", loginCookies);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var tokenStart = html.IndexOf("name=\"__RequestVerificationToken\"", StringComparison.Ordinal);
        if (tokenStart == -1) return string.Empty;

        var valueStart = html.IndexOf("value=\"", tokenStart, StringComparison.Ordinal) + 7;
        var valueEnd = html.IndexOf("\"", valueStart, StringComparison.Ordinal);
        return html[valueStart..valueEnd];
    }

    private async Task<(string Token, string Cookies)> GetFormTokenAndCookies(string url, string authCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", authCookie);
        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(content);

        var allCookies = new List<string> { authCookie };
        if (response.Headers.TryGetValues("Set-Cookie", out var responseCookies))
        {
            allCookies.AddRange(responseCookies);
        }

        return (token, string.Join("; ", allCookies));
    }

    private async Task<string> CreateAgentViaService(string displayName = "Test Agent", string? description = null)
    {
        using var scope = _factory.Services.CreateScope();
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        var result = await adminService.CreateAgentUser(displayName, description, admin!.Id);
        return result.Data!.User.Id;
    }

    // --- 33.1 Agent List Page ---

    [Fact]
    public async Task Agents_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Admin/Agents");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Agents_NonAdmin_RedirectsToAccessDenied()
    {
        var (_, cookie) = await CreateAndLoginUser("agent-nonadmin@test.com", "Non Admin");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Agents");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Agents_Admin_ReturnsSuccess()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Agents");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Agent Management", content);
    }

    [Fact]
    public async Task Agents_Admin_EmptyState_ShowsMessage()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Agents");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Create Agent", content);
    }

    [Fact]
    public async Task Agents_Admin_WithAgents_ShowsAgentInList()
    {
        await CreateAgentViaService("List Test Agent", "A test agent for listing");

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Agents");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("List Test Agent", content);
        Assert.Contains("Active", content);
    }

    [Fact]
    public async Task Dashboard_Admin_ShowsAgentsLink()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Dashboard");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Agent Management", content);
    }

    // --- 33.2 Agent Creation Flow ---

    [Fact]
    public async Task CreateAgent_Get_Admin_ReturnsForm()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/CreateAgent");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Create Agent", content);
        Assert.Contains("Display Name", content);
    }

    [Fact]
    public async Task CreateAgent_Post_Admin_Success_ShowsToken()
    {
        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies("/Admin/CreateAgent", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/CreateAgent");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["DisplayName"] = "Creation Test Agent",
            ["Description"] = "An agent for testing creation",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Agent Created Successfully", content);
        Assert.Contains("Creation Test Agent", content);
        Assert.Contains("will not be shown again", content);
        Assert.Contains("tw_", content);
    }

    [Fact]
    public async Task CreateAgent_Post_Admin_EmptyName_ReturnsFormWithErrors()
    {
        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies("/Admin/CreateAgent", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/CreateAgent");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["DisplayName"] = "",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Display name is required", content);
    }

    // --- 33.3 Agent Edit and Detail Pages ---

    [Fact]
    public async Task AgentDetail_Admin_ReturnsSuccess()
    {
        var agentId = await CreateAgentViaService("Detail Test Agent", "Agent for detail page testing");

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/AgentDetail?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Detail Test Agent", content);
        Assert.Contains("Agent for detail page testing", content);
        Assert.Contains("API Tokens", content);
        Assert.Contains("Project Memberships", content);
    }

    [Fact]
    public async Task AgentDetail_Admin_NonExistent_RedirectsToAgents()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/AgentDetail?id=nonexistent-id");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Agents", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task EditAgent_Get_Admin_ReturnsForm()
    {
        var agentId = await CreateAgentViaService("Edit Form Agent");

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Edit Agent", content);
        Assert.Contains("Edit Form Agent", content);
    }

    [Fact]
    public async Task EditAgent_Post_Admin_Success_RedirectsToDetail()
    {
        var agentId = await CreateAgentViaService("Edit Submit Agent");

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/EditAgent");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = agentId,
            ["DisplayName"] = "Updated Agent Name",
            ["Description"] = "Updated description",
            ["IsActive"] = "true",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/AgentDetail", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task EditAgent_Get_Admin_NonAgent_RedirectsToAgents()
    {
        var (userId, _) = await CreateAndLoginUser("edit-nonagent@test.com", "Not An Agent");

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Agents", response.Headers.Location?.ToString());
    }

    // --- 33.4 Agent Pause/Resume and Deletion ---

    [Fact]
    public async Task ToggleAgentActive_Admin_Pause_RedirectsToAgents()
    {
        var agentId = await CreateAgentViaService("Pause Test Agent");

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies("/Admin/Agents", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ToggleAgentActive?id={agentId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Agents", response.Headers.Location?.ToString());

        // Verify agent is now paused
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var agent = await userManager.FindByIdAsync(agentId);
        Assert.False(agent!.IsAgentActive);
    }

    [Fact]
    public async Task ToggleAgentActive_Admin_Resume_RedirectsToAgents()
    {
        var agentId = await CreateAgentViaService("Resume Test Agent");

        // First pause the agent
        using (var scope = _factory.Services.CreateScope())
        {
            var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
            await adminService.SetAgentActive(agentId, false, admin!.Id);
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies("/Admin/Agents", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ToggleAgentActive?id={agentId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Agents", response.Headers.Location?.ToString());

        // Verify agent is now active again
        using var verifyScope = _factory.Services.CreateScope();
        var verifyUserManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var agent = await verifyUserManager.FindByIdAsync(agentId);
        Assert.True(agent!.IsAgentActive);
    }

    [Fact]
    public async Task DeleteAgent_Admin_RedirectsToAgents()
    {
        var agentId = await CreateAgentViaService("Delete Test Agent");

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/DeleteAgent?id={agentId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Agents", response.Headers.Location?.ToString());

        // Verify agent is deleted
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var agent = await userManager.FindByIdAsync(agentId);
        Assert.Null(agent);
    }

    [Fact]
    public async Task ToggleAgentActive_NonAgent_RedirectsToAgents()
    {
        var (userId, _) = await CreateAndLoginUser("toggle-nonagent@test.com", "Not An Agent");

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies("/Admin/Agents", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ToggleAgentActive?id={userId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Agents", response.Headers.Location?.ToString());
    }

    // --- PAT management from agent detail page ---

    [Fact]
    public async Task GenerateAgentToken_Admin_RedirectsToDetail()
    {
        var agentId = await CreateAgentViaService("Token Gen Agent");

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/AgentDetail?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/GenerateAgentToken?id={agentId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/AgentDetail", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task RevokeAgentToken_Admin_RedirectsToDetail()
    {
        var agentId = await CreateAgentViaService("Token Revoke Agent");

        // Get token ID
        using var scope = _factory.Services.CreateScope();
        var patService = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
        var tokens = await patService.GetTokensForUserAsync(agentId);
        var tokenId = tokens.Data!.First().Id;

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/AgentDetail?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/RevokeAgentToken?userId={agentId}&tokenId={tokenId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/AgentDetail", response.Headers.Location?.ToString());
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
