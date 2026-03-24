using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Security;

public class AiSecurityAuditTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiSecurityAuditTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email = "ai-sec@test.com", string displayName = "AI Security User")
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.EnsureCreatedAsync();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            return (existing.Id, await GetLoginCookie(email));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };
        await userManager.CreateAsync(user, "TestPass1!");

        return (user.Id, await GetLoginCookie(email));
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

    private async Task<(string Cookie, string Token)> GetAuthenticatedRequestParts(string email = "ai-sec@test.com")
    {
        var (_, loginCookie) = await CreateAndLoginUser(email);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var content = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(content);

        var allCookies = loginCookie;
        if (getResponse.Headers.Contains("Set-Cookie"))
        {
            allCookies += "; " + string.Join("; ", getResponse.Headers.GetValues("Set-Cookie"));
        }

        return (allCookies, token);
    }

    private async Task<(int ProjectId, int TaskId)> CreateProjectAndTask(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project
        {
            Name = $"AI Security Test Project {Guid.NewGuid():N}",
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

        var task = new TaskItem
        {
            Title = "Security Test Task",
            ProjectId = project.Id,
            CreatedByUserId = userId,
            Status = TaskItemStatus.ToDo,
            Priority = TaskItemPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        return (project.Id, task.Id);
    }

    private async Task<int> CreateInboxItem(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var item = new InboxItem
        {
            Title = "Security Test Inbox",
            UserId = userId,
            Status = InboxItemStatus.Unprocessed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.InboxItems.Add(item);
        await context.SaveChangesAsync();

        return item.Id;
    }

    // --- Authentication enforcement on ALL AI endpoints ---

    [Theory]
    [InlineData("POST", "/Ai/RewriteProjectDescription")]
    [InlineData("POST", "/Ai/RewriteTaskDescription")]
    [InlineData("POST", "/Ai/PolishComment")]
    [InlineData("POST", "/Ai/ExpandInboxItem")]
    [InlineData("POST", "/Ai/ProjectSummary")]
    [InlineData("POST", "/Ai/PersonalDigest")]
    [InlineData("POST", "/Ai/ReviewPreparation")]
    [InlineData("GET", "/Ai/IsAvailable")]
    public async Task AllAiEndpoints_RequireAuthentication(string method, string url)
    {
        HttpResponseMessage response;
        if (method == "GET")
        {
            response = await _client.GetAsync(url);
        }
        else
        {
            response = await _client.PostAsync(url, new FormUrlEncodedContent([]));
        }

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- Anti-forgery token enforcement on all POST endpoints ---

    [Theory]
    [InlineData("/Ai/RewriteProjectDescription")]
    [InlineData("/Ai/RewriteTaskDescription")]
    [InlineData("/Ai/PolishComment")]
    [InlineData("/Ai/ExpandInboxItem")]
    [InlineData("/Ai/ProjectSummary")]
    [InlineData("/Ai/PersonalDigest")]
    [InlineData("/Ai/ReviewPreparation")]
    public async Task AllPostEndpoints_RequireAntiForgeryToken(string url)
    {
        var (_, cookie) = await CreateAndLoginUser("ai-sec-antiforgery@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["description"] = "test"
        });

        var response = await _client.SendAsync(request);

        // Without anti-forgery token, the request should be rejected (400 Bad Request)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Input length validation on rewrite endpoints ---

    [Fact]
    public async Task RewriteProjectDescription_RejectsExcessiveInput()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts("ai-sec-length1@test.com");
        var (userId, _) = await CreateAndLoginUser("ai-sec-length1@test.com");
        var (projectId, _) = await CreateProjectAndTask(userId);

        var longText = new string('A', 4001);
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/RewriteProjectDescription");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["projectId"] = projectId.ToString(),
            ["description"] = longText,
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("characters or fewer", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RewriteTaskDescription_RejectsExcessiveInput()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts("ai-sec-length2@test.com");
        var (userId, _) = await CreateAndLoginUser("ai-sec-length2@test.com");
        var (_, taskId) = await CreateProjectAndTask(userId);

        var longText = new string('A', 4001);
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/RewriteTaskDescription");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["taskId"] = taskId.ToString(),
            ["description"] = longText,
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("characters or fewer", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PolishComment_RejectsExcessiveInput()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts("ai-sec-length3@test.com");
        var (userId, _) = await CreateAndLoginUser("ai-sec-length3@test.com");
        var (_, taskId) = await CreateProjectAndTask(userId);

        var longText = new string('A', 4001);
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/PolishComment");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["taskId"] = taskId.ToString(),
            ["comment"] = longText,
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("characters or fewer", content, StringComparison.OrdinalIgnoreCase);
    }

    // --- Resource-level authorization

    [Fact]
    public async Task RewriteProjectDescription_NonMember_ReturnsAccessDenied()
    {
        var (ownerUserId, _) = await CreateAndLoginUser("ai-sec-owner1@test.com", "Owner");
        var (projectId, _) = await CreateProjectAndTask(ownerUserId);

        var (cookie, token) = await GetAuthenticatedRequestParts("ai-sec-nonmember1@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/RewriteProjectDescription");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["projectId"] = projectId.ToString(),
            ["description"] = "Test description",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("denied", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RewriteTaskDescription_NonMember_ReturnsAccessDenied()
    {
        var (ownerUserId, _) = await CreateAndLoginUser("ai-sec-owner2@test.com", "Owner");
        var (_, taskId) = await CreateProjectAndTask(ownerUserId);

        var (cookie, token) = await GetAuthenticatedRequestParts("ai-sec-nonmember2@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/RewriteTaskDescription");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["taskId"] = taskId.ToString(),
            ["description"] = "Test description",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("denied", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectSummary_NonMember_ReturnsAccessDenied()
    {
        var (ownerUserId, _) = await CreateAndLoginUser("ai-sec-owner3@test.com", "Owner");
        var (projectId, _) = await CreateProjectAndTask(ownerUserId);

        var (cookie, token) = await GetAuthenticatedRequestParts("ai-sec-nonmember3@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/ProjectSummary");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["projectId"] = projectId.ToString(),
            ["period"] = "ThisWeek",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("denied", content, StringComparison.OrdinalIgnoreCase);
    }

    // --- Inbox item ownership enforcement ---

    [Fact]
    public async Task ExpandInboxItem_NonOwner_ReturnsAccessDenied()
    {
        var (ownerUserId, _) = await CreateAndLoginUser("ai-sec-inbox-owner@test.com", "Inbox Owner");
        var inboxItemId = await CreateInboxItem(ownerUserId);

        var (cookie, token) = await GetAuthenticatedRequestParts("ai-sec-inbox-thief@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/ExpandInboxItem");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["inboxItemId"] = inboxItemId.ToString(),
            ["title"] = "Some title",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("denied", content, StringComparison.OrdinalIgnoreCase);
    }

    // --- GlobalConfiguration Ollama keys are admin-only ---

    [Fact]
    public async Task GlobalConfiguration_OllamaKeys_AdminOnlyAccess()
    {
        var (_, cookie) = await CreateAndLoginUser("ai-sec-nonadmin@test.com", "Non Admin");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Configuration");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        // Non-admin users should be redirected to Access Denied or login
        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected Redirect or Forbidden, got {response.StatusCode}");
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
