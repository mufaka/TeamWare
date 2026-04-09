using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Controllers;

public class AdminControllerAgentConfigTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminControllerAgentConfigTests(TeamWareWebApplicationFactory factory)
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

    private async Task<string> CreateAgentViaService(string displayName = "Config Test Agent")
    {
        using var scope = _factory.Services.CreateScope();
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        var result = await adminService.CreateAgentUser(displayName, null, admin!.Id);
        return result.Data!.User.Id;
    }

    private async Task<string> CreateProjectForAdmin(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);
        var result = await projectService.CreateProject(name, null, admin!.Id);
        Assert.True(result.Succeeded);
        return result.Data!.Name;
    }

    // --- 45.1 Configuration Section ---

    [Fact]
    public async Task EditAgent_Get_ShowsConfigurationSection()
    {
        var agentId = await CreateAgentViaService("Config Section Agent");

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Configuration", content);
        Assert.Contains("Use default polling interval", content);
        Assert.Contains("Use default model", content);
        Assert.Contains("Use default auto-approve", content);
        Assert.Contains("Use default dry run", content);
        Assert.Contains("Use default task timeout", content);
        Assert.Contains("Use default system prompt", content);
    }

    [Fact]
    public async Task EditAgent_Post_SavesConfiguration_RedirectsToDetail()
    {
        var agentId = await CreateAgentViaService("Config Save Agent");

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/EditAgent");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = agentId,
            ["DisplayName"] = "Config Save Agent",
            ["IsActive"] = "true",
            ["Configuration.PollingIntervalUseDefault"] = "false",
            ["Configuration.PollingIntervalSeconds"] = "120",
            ["Configuration.ModelUseDefault"] = "false",
            ["Configuration.Model"] = "gpt-4o",
            ["Configuration.AutoApproveToolsUseDefault"] = "true",
            ["Configuration.DryRunUseDefault"] = "true",
            ["Configuration.TaskTimeoutUseDefault"] = "true",
            ["Configuration.SystemPromptUseDefault"] = "true",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/AgentDetail", response.Headers.Location?.ToString());

        // Verify config was saved
        using var scope = _factory.Services.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await configService.GetConfigurationAsync(agentId);
        Assert.True(configResult.Succeeded);
        Assert.NotNull(configResult.Data);
        Assert.Equal(120, configResult.Data!.PollingIntervalSeconds);
        Assert.Equal("gpt-4o", configResult.Data.Model);
    }

    [Fact]
    public async Task EditAgent_Post_UseDefault_SavesNullValues()
    {
        var agentId = await CreateAgentViaService("Default Config Agent");

        // First save with custom values
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.SaveConfigurationAsync(agentId, new Web.ViewModels.SaveAgentConfigurationDto
            {
                PollingIntervalSeconds = 120,
                Model = "gpt-4o"
            });
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/EditAgent");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = agentId,
            ["DisplayName"] = "Default Config Agent",
            ["IsActive"] = "true",
            ["Configuration.PollingIntervalUseDefault"] = "true",
            ["Configuration.ModelUseDefault"] = "true",
            ["Configuration.AutoApproveToolsUseDefault"] = "true",
            ["Configuration.DryRunUseDefault"] = "true",
            ["Configuration.TaskTimeoutUseDefault"] = "true",
            ["Configuration.SystemPromptUseDefault"] = "true",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Verify values were cleared to null
        using var verifyScope = _factory.Services.CreateScope();
        var verifyService = verifyScope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.True(configResult.Succeeded);
        Assert.NotNull(configResult.Data);
        Assert.Null(configResult.Data!.PollingIntervalSeconds);
        Assert.Null(configResult.Data.Model);
    }

    [Fact]
    public async Task EditAgent_Get_LoadsExistingConfiguration()
    {
        var agentId = await CreateAgentViaService("Load Config Agent");

        // Save config via service
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.SaveConfigurationAsync(agentId, new Web.ViewModels.SaveAgentConfigurationDto
            {
                PollingIntervalSeconds = 90,
                Model = "claude-sonnet"
            });
        }

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("90", content);
        Assert.Contains("claude-sonnet", content);
    }

    // --- 45.2 Repository Section ---

    [Fact]
    public async Task EditAgent_Get_ShowsRepositoriesSection()
    {
        var agentId = await CreateAgentViaService("Repo Section Agent");
        await CreateProjectForAdmin("Repository Dropdown Project");

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Repositories", content);
        Assert.Contains("No repositories configured", content);
        Assert.Contains("Add Repository", content);
        Assert.Contains("Select a project", content);
        Assert.Contains("Repository Dropdown Project", content);
        Assert.Contains("<select name=\"ProjectName\"", content);
    }

    [Fact]
    public async Task AddAgentRepository_Success_RedirectsToEditAgent()
    {
        var agentId = await CreateAgentViaService("Add Repo Agent");
        var projectName = await CreateProjectForAdmin("TestProject");

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/AddAgentRepository?userId={agentId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectName"] = projectName,
            ["Url"] = "https://github.com/test/repo",
            ["Branch"] = "main",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/EditAgent", response.Headers.Location?.ToString());

        // Verify repository was added
        using var scope = _factory.Services.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await configService.GetConfigurationAsync(agentId);
        Assert.True(configResult.Succeeded);
        Assert.NotNull(configResult.Data);
        Assert.Single(configResult.Data!.Repositories);
        Assert.Equal("TestProject", configResult.Data.Repositories[0].ProjectName);
    }

    [Fact]
    public async Task RemoveAgentRepository_Success_RedirectsToEditAgent()
    {
        var agentId = await CreateAgentViaService("Remove Repo Agent");

        // Add a repository first
        int repoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var addResult = await configService.AddRepositoryAsync(agentId, new Web.ViewModels.SaveAgentRepositoryDto
            {
                ProjectName = "ToRemove",
                Url = "https://github.com/test/remove",
                Branch = "main"
            });
            repoId = addResult.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/RemoveAgentRepository?userId={agentId}&repositoryId={repoId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/EditAgent", response.Headers.Location?.ToString());

        // Verify repository was removed
        using var verifyScope = _factory.Services.CreateScope();
        var verifyService = verifyScope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.True(configResult.Succeeded);
        // Config may or may not exist, but if it does, no repos
        if (configResult.Data != null)
        {
            Assert.Empty(configResult.Data.Repositories);
        }
    }

    [Fact]
    public async Task EditAgent_ShowsRepositoryInTable()
    {
        var agentId = await CreateAgentViaService("Show Repo Agent");

        // Add a repository via service
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.AddRepositoryAsync(agentId, new Web.ViewModels.SaveAgentRepositoryDto
            {
                ProjectName = "VisibleProject",
                Url = "https://github.com/visible/repo",
                Branch = "develop"
            });
        }

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("VisibleProject", content);
        Assert.Contains("https://github.com/visible/repo", content);
        Assert.Contains("develop", content);
    }

    // --- 45.3 MCP Servers Section ---

    [Fact]
    public async Task EditAgent_Get_ShowsMcpServersSection()
    {
        var agentId = await CreateAgentViaService("MCP Section Agent");

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("MCP Servers", content);
        Assert.Contains("No MCP servers configured", content);
        Assert.Contains("Add MCP Server", content);
    }

    [Fact]
    public async Task AddAgentMcpServer_Success_RedirectsToEditAgent()
    {
        var agentId = await CreateAgentViaService("Add MCP Agent");

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/AddAgentMcpServer?userId={agentId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "test-mcp",
            ["Type"] = "http",
            ["Url"] = "https://mcp.example.com",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/EditAgent", response.Headers.Location?.ToString());

        // Verify MCP server was added
        using var scope = _factory.Services.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await configService.GetConfigurationAsync(agentId);
        Assert.True(configResult.Succeeded);
        Assert.NotNull(configResult.Data);
        Assert.Single(configResult.Data!.McpServers);
        Assert.Equal("test-mcp", configResult.Data.McpServers[0].Name);
    }

    [Fact]
    public async Task RemoveAgentMcpServer_Success_RedirectsToEditAgent()
    {
        var agentId = await CreateAgentViaService("Remove MCP Agent");

        int serverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var addResult = await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "to-remove-mcp",
                Type = "http",
                Url = "https://mcp.example.com"
            });
            serverId = addResult.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/RemoveAgentMcpServer?userId={agentId}&mcpServerId={serverId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/EditAgent", response.Headers.Location?.ToString());

        // Verify MCP server was removed
        using var verifyScope = _factory.Services.CreateScope();
        var verifyService = verifyScope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        if (configResult.Data != null)
        {
            Assert.Empty(configResult.Data.McpServers);
        }
    }

    [Fact]
    public async Task EditAgent_ShowsMcpServerInTable()
    {
        var agentId = await CreateAgentViaService("Show MCP Agent");

        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "visible-mcp",
                Type = "stdio",
                Command = "npx"
            });
        }

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("visible-mcp", content);
        Assert.Contains("stdio", content);
        Assert.Contains("npx", content);
    }

    // --- 45.4 Agent Detail Page ---

    [Fact]
    public async Task AgentDetail_NoConfig_ShowsDefaultMessage()
    {
        var agentId = await CreateAgentViaService("Detail NoConfig Agent");

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/AgentDetail?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Configuration", content);
        Assert.Contains("Using agent defaults", content);
        Assert.Contains("None configured", content);
    }

    [Fact]
    public async Task AgentDetail_WithConfig_ShowsConfigValues()
    {
        var agentId = await CreateAgentViaService("Detail Config Agent");

        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.SaveConfigurationAsync(agentId, new Web.ViewModels.SaveAgentConfigurationDto
            {
                PollingIntervalSeconds = 90,
                Model = "gpt-4o-mini"
            });
        }

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/AgentDetail?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("90s", content);
        Assert.Contains("gpt-4o-mini", content);
    }

    [Fact]
    public async Task AgentDetail_WithRepos_ShowsRepositories()
    {
        var agentId = await CreateAgentViaService("Detail Repos Agent");

        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.AddRepositoryAsync(agentId, new Web.ViewModels.SaveAgentRepositoryDto
            {
                ProjectName = "DetailTestProject",
                Url = "https://github.com/detail/test",
                Branch = "main"
            });
        }

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/AgentDetail?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("DetailTestProject", content);
        Assert.Contains("https://github.com/detail/test", content);
    }

    [Fact]
    public async Task AgentDetail_WithMcpServers_ShowsMcpServers()
    {
        var agentId = await CreateAgentViaService("Detail MCP Agent");

        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "detail-mcp-server",
                Type = "http",
                Url = "https://detail.mcp.example.com"
            });
        }

        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/AgentDetail?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("detail-mcp-server", content);
        Assert.Contains("http", content);
    }

    // --- 51.2 Repository Edit UI ---

    [Fact]
    public async Task EditAgent_Get_ShowsEditButtonForRepository()
    {
        var agentId = await CreateAgentViaService("Edit Btn Repo Agent");

        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.AddRepositoryAsync(agentId, new Web.ViewModels.SaveAgentRepositoryDto
            {
                ProjectName = "EditBtnProject",
                Url = "https://github.com/editbtn/repo",
                Branch = "main"
            });
        }

        var cookie = await LoginAsAdmin();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-edit-btn=", content);
        Assert.Contains("UpdateAgentRepository", content);
    }

    [Fact]
    public async Task UpdateAgentRepository_Success_RedirectsToEditAgent()
    {
        var agentId = await CreateAgentViaService("Update Repo Agent");
        var projectName = await CreateProjectForAdmin("UpdateRepoProject");

        int repoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddRepositoryAsync(agentId, new Web.ViewModels.SaveAgentRepositoryDto
            {
                ProjectName = projectName,
                Url = "https://github.com/old/url",
                Branch = "main"
            });
            repoId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentRepository?userId={agentId}&repositoryId={repoId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectName"] = projectName,
            ["Url"] = "https://github.com/new/url",
            ["Branch"] = "develop",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/EditAgent", response.Headers.Location?.ToString());

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.True(configResult.Succeeded);
        var repo = configResult.Data!.Repositories.Single();
        Assert.Equal("https://github.com/new/url", repo.Url);
        Assert.Equal("develop", repo.Branch);
    }

    [Fact]
    public async Task UpdateAgentRepository_BlankToken_PreservesExistingToken()
    {
        var agentId = await CreateAgentViaService("Blank Token Repo Agent");
        var projectName = await CreateProjectForAdmin("BlankTokenProject");

        int repoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddRepositoryAsync(agentId, new Web.ViewModels.SaveAgentRepositoryDto
            {
                ProjectName = projectName,
                Url = "https://github.com/token/repo",
                Branch = "main",
                AccessToken = "secret-token"
            });
            repoId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        // POST with blank AccessToken and ClearAccessToken not set
        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentRepository?userId={agentId}&repositoryId={repoId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectName"] = projectName,
            ["Url"] = "https://github.com/token/repo",
            ["Branch"] = "main",
            ["AccessToken"] = "",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.True(configResult.Succeeded);
        // Masked token should still be present (non-null) indicating the secret was preserved
        Assert.NotNull(configResult.Data!.Repositories.Single().AccessToken);
    }

    [Fact]
    public async Task UpdateAgentRepository_ClearToken_NullsStoredToken()
    {
        var agentId = await CreateAgentViaService("Clear Token Repo Agent");
        var projectName = await CreateProjectForAdmin("ClearTokenProject");

        int repoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddRepositoryAsync(agentId, new Web.ViewModels.SaveAgentRepositoryDto
            {
                ProjectName = projectName,
                Url = "https://github.com/clear/repo",
                Branch = "main",
                AccessToken = "to-be-cleared"
            });
            repoId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentRepository?userId={agentId}&repositoryId={repoId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectName"] = projectName,
            ["Url"] = "https://github.com/clear/repo",
            ["Branch"] = "main",
            ["ClearAccessToken"] = "true",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.True(configResult.Succeeded);
        Assert.Null(configResult.Data!.Repositories.Single().AccessToken);
    }

    // --- 51.3 MCP Server Edit UI ---

    [Fact]
    public async Task EditAgent_Get_ShowsEditButtonForMcpServer()
    {
        var agentId = await CreateAgentViaService("Edit Btn MCP Agent");

        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "edit-btn-mcp",
                Type = "http",
                Url = "https://mcp.example.com"
            });
        }

        var cookie = await LoginAsAdmin();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/EditAgent?id={agentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-edit-btn=", content);
        Assert.Contains("UpdateAgentMcpServer", content);
    }

    [Fact]
    public async Task UpdateAgentMcpServer_Http_Success_RedirectsToEditAgent()
    {
        var agentId = await CreateAgentViaService("Update HTTP MCP Agent");

        int serverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "old-http-mcp",
                Type = "http",
                Url = "https://old.mcp.example.com"
            });
            serverId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentMcpServer?userId={agentId}&mcpServerId={serverId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "new-http-mcp",
            ["Type"] = "http",
            ["Url"] = "https://new.mcp.example.com",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/EditAgent", response.Headers.Location?.ToString());

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        var server = configResult.Data!.McpServers.Single();
        Assert.Equal("new-http-mcp", server.Name);
        Assert.Equal("https://new.mcp.example.com", server.Url);
    }

    [Fact]
    public async Task UpdateAgentMcpServer_Stdio_Success_RedirectsToEditAgent()
    {
        var agentId = await CreateAgentViaService("Update Stdio MCP Agent");

        int serverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "old-stdio-mcp",
                Type = "stdio",
                Command = "node"
            });
            serverId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentMcpServer?userId={agentId}&mcpServerId={serverId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "new-stdio-mcp",
            ["Type"] = "stdio",
            ["Command"] = "npx",
            ["Args"] = "[\"-y\", \"mcp-server\"]",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/EditAgent", response.Headers.Location?.ToString());

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        var server = configResult.Data!.McpServers.Single();
        Assert.Equal("new-stdio-mcp", server.Name);
        Assert.Equal("npx", server.Command);
    }

    [Fact]
    public async Task UpdateAgentMcpServer_BlankAuthHeader_PreservesExistingAuthHeader()
    {
        var agentId = await CreateAgentViaService("Blank Auth MCP Agent");

        int serverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "auth-mcp",
                Type = "http",
                Url = "https://auth.mcp.example.com",
                AuthHeader = "Bearer secret-token"
            });
            serverId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentMcpServer?userId={agentId}&mcpServerId={serverId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "auth-mcp",
            ["Type"] = "http",
            ["Url"] = "https://auth.mcp.example.com",
            ["AuthHeader"] = "",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.NotNull(configResult.Data!.McpServers.Single().AuthHeader);
    }

    [Fact]
    public async Task UpdateAgentMcpServer_ClearAuthHeader_NullsStoredAuthHeader()
    {
        var agentId = await CreateAgentViaService("Clear Auth MCP Agent");

        int serverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "clear-auth-mcp",
                Type = "http",
                Url = "https://clearauth.mcp.example.com",
                AuthHeader = "Bearer to-be-cleared"
            });
            serverId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentMcpServer?userId={agentId}&mcpServerId={serverId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "clear-auth-mcp",
            ["Type"] = "http",
            ["Url"] = "https://clearauth.mcp.example.com",
            ["ClearAuthHeader"] = "true",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.Null(configResult.Data!.McpServers.Single().AuthHeader);
    }

    [Fact]
    public async Task UpdateAgentMcpServer_BlankEnv_PreservesExistingEnv()
    {
        var agentId = await CreateAgentViaService("Blank Env MCP Agent");

        int serverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "env-mcp",
                Type = "stdio",
                Command = "npx",
                Env = "{\"API_KEY\": \"secret\"}"
            });
            serverId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentMcpServer?userId={agentId}&mcpServerId={serverId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "env-mcp",
            ["Type"] = "stdio",
            ["Command"] = "npx",
            ["Env"] = "",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.NotNull(configResult.Data!.McpServers.Single().Env);
    }

    [Fact]
    public async Task UpdateAgentMcpServer_ClearEnv_NullsStoredEnv()
    {
        var agentId = await CreateAgentViaService("Clear Env MCP Agent");

        int serverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
            var result = await configService.AddMcpServerAsync(agentId, new Web.ViewModels.SaveAgentMcpServerDto
            {
                Name = "clear-env-mcp",
                Type = "stdio",
                Command = "npx",
                Env = "{\"KEY\": \"to-be-cleared\"}"
            });
            serverId = result.Data;
        }

        var adminCookie = await LoginAsAdmin();
        var (antiForgeryToken, cookies) = await GetFormTokenAndCookies($"/Admin/EditAgent?id={agentId}", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UpdateAgentMcpServer?userId={agentId}&mcpServerId={serverId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "clear-env-mcp",
            ["Type"] = "stdio",
            ["Command"] = "npx",
            ["ClearEnv"] = "true",
            ["DisplayOrder"] = "0",
            ["__RequestVerificationToken"] = antiForgeryToken
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyService = scope2.ServiceProvider.GetRequiredService<IAgentConfigurationService>();
        var configResult = await verifyService.GetConfigurationAsync(agentId);
        Assert.Null(configResult.Data!.McpServers.Single().Env);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
