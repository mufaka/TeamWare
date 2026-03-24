using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Views;

public class AiSummaryUiTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiSummaryUiTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(string Cookie, string UserId)> CreateAndLoginUser(string email = "ai-summary-ui@test.com")
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.EnsureCreatedAsync();

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = "AI Summary UI Test User"
            };
            await userManager.CreateAsync(user, "TestPass1!");
        }

        var cookie = await GetLoginCookie(email);
        return (cookie, user.Id);
    }

    private async Task<string> GetLoginCookie(string email)
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
            ["Password"] = "TestPass1!",
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

    private async Task<int> CreateProject(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project
        {
            Name = $"AI Summary UI Test Project {Guid.NewGuid():N}",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return project.Id;
    }

    private async Task SetOllamaUrl(string url)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var config = await context.GlobalConfigurations
            .FirstOrDefaultAsync(c => c.Key == "OLLAMA_URL");
        if (config != null)
        {
            config.Value = url;
            await context.SaveChangesAsync();
        }

        // Clear the OllamaService config cache so changes take effect immediately
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        cache.Remove("OllamaConfig_OLLAMA_URL");
    }

    private async Task<string> GetAuthenticatedPage(string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // --- Summary button NOT rendered when OLLAMA_URL is empty ---

    [Fact]
    public async Task ProjectDetails_NoSummaryButton_WhenOllamaNotConfigured()
    {
        var (cookie, userId) = await CreateAndLoginUser();
        var projectId = await CreateProject(userId);

        var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);

        Assert.DoesNotContain("data-ai-summary", html);
        Assert.DoesNotContain("ai-summary.js", html);
    }

    [Fact]
    public async Task Dashboard_NoSummaryButton_WhenOllamaNotConfigured()
    {
        var (cookie, _) = await CreateAndLoginUser();

        var html = await GetAuthenticatedPage("/", cookie);

        Assert.DoesNotContain("data-ai-summary", html);
        Assert.DoesNotContain("ai-summary.js", html);
    }

    [Fact]
    public async Task Review_NoSummaryButton_WhenOllamaNotConfigured()
    {
        var (cookie, _) = await CreateAndLoginUser();

        var html = await GetAuthenticatedPage("/Review", cookie);

        Assert.DoesNotContain("data-ai-summary", html);
        Assert.DoesNotContain("ai-summary.js", html);
    }

    // --- Summary button IS rendered when OLLAMA_URL is configured ---

    [Fact]
    public async Task ProjectDetails_HasSummaryButton_WhenOllamaConfigured()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-summary-configured1@test.com");
        var projectId = await CreateProject(userId);
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);

            Assert.Contains("data-ai-summary", html);
            Assert.Contains("ai-summary.js", html);
            Assert.Contains("ProjectSummary", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Dashboard_HasSummaryButton_WhenOllamaConfigured()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-summary-configured2@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);

            Assert.Contains("data-ai-summary", html);
            Assert.Contains("ai-summary.js", html);
            Assert.Contains("PersonalDigest", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_HasSummaryButton_WhenOllamaConfigured()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-summary-configured3@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);

            Assert.Contains("data-ai-summary", html);
            Assert.Contains("ai-summary.js", html);
            Assert.Contains("ReviewPreparation", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
