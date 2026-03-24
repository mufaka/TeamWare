using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class AiControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "ai-test@test.com")
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.EnsureCreatedAsync();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing == null)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = "AI Test User"
            };
            await userManager.CreateAsync(user, "TestPass1!");
        }

        return await GetLoginCookie(email);
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

    private async Task<(string Cookie, string Token)> GetAuthenticatedRequestParts(string email = "ai-test@test.com")
    {
        var loginCookie = await CreateAndLoginUser(email);

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

    private async Task<(int ProjectId, int TaskId)> CreateProjectAndTask(string email = "ai-test@test.com")
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email);

        var project = new Project
        {
            Name = $"AI Test Project {Guid.NewGuid():N}",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user!.Id,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var task = new TaskItem
        {
            Title = "AI Test Task",
            ProjectId = project.Id,
            CreatedByUserId = user.Id,
            Status = TaskItemStatus.ToDo,
            Priority = TaskItemPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        return (project.Id, task.Id);
    }

    private async Task<int> CreateInboxItem(string email = "ai-test@test.com")
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email);

        var inboxItem = new InboxItem
        {
            Title = "AI Test Inbox Item",
            Description = "Some description",
            UserId = user!.Id,
            Status = InboxItemStatus.Unprocessed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.InboxItems.Add(inboxItem);
        await context.SaveChangesAsync();

        return inboxItem.Id;
    }

    private async Task<HttpResponseMessage> PostWithAuth(string url, Dictionary<string, string> formData, string cookie, string token)
    {
        formData["__RequestVerificationToken"] = token;
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(formData);
        return await _client.SendAsync(request);
    }

    // --- Authentication Required ---

    [Fact]
    public async Task IsAvailable_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Ai/IsAvailable");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task RewriteProjectDescription_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/RewriteProjectDescription");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task RewriteTaskDescription_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/RewriteTaskDescription");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PolishComment_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/PolishComment");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ExpandInboxItem_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/ExpandInboxItem");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ProjectSummary_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/ProjectSummary");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PersonalDigest_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/PersonalDigest");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ReviewPreparation_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/ReviewPreparation");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- IsAvailable returns false when OLLAMA_URL is empty ---

    [Fact]
    public async Task IsAvailable_ReturnsAvailableFalse_WhenOllamaUrlIsEmpty()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Ai/IsAvailable");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"available\":false", content);
    }

    // --- RewriteProjectDescription authorization ---

    [Fact]
    public async Task RewriteProjectDescription_NonMember_ReturnsAccessDenied()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/RewriteProjectDescription", new Dictionary<string, string>
        {
            ["projectId"] = "999999",
            ["description"] = "Some description"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("Access denied", json);
    }

    [Fact]
    public async Task RewriteProjectDescription_EmptyDescription_ReturnsError()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/RewriteProjectDescription", new Dictionary<string, string>
        {
            ["projectId"] = "1",
            ["description"] = ""
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("required", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RewriteProjectDescription_TooLongDescription_ReturnsError()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();
        var longDescription = new string('a', 4001);

        var response = await PostWithAuth("/Ai/RewriteProjectDescription", new Dictionary<string, string>
        {
            ["projectId"] = "1",
            ["description"] = longDescription
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("4000", json);
    }

    [Fact]
    public async Task RewriteProjectDescription_Member_OllamaNotConfigured_ReturnsFailure()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();
        var (projectId, _) = await CreateProjectAndTask();

        var response = await PostWithAuth("/Ai/RewriteProjectDescription", new Dictionary<string, string>
        {
            ["projectId"] = projectId.ToString(),
            ["description"] = "A valid description"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not configured", json, StringComparison.OrdinalIgnoreCase);
    }

    // --- RewriteTaskDescription authorization ---

    [Fact]
    public async Task RewriteTaskDescription_NonMember_ReturnsAccessDenied()
    {
        // Create project+task owned by the default user first
        await CreateAndLoginUser();
        var (_, taskId) = await CreateProjectAndTask();

        // Then login as a different (non-member) user and try to access
        var (cookie, token) = await GetAuthenticatedRequestParts("ai-nonmember@test.com");

        var response = await PostWithAuth("/Ai/RewriteTaskDescription", new Dictionary<string, string>
        {
            ["taskId"] = taskId.ToString(),
            ["description"] = "Some description"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("Access denied", json);
    }

    [Fact]
    public async Task RewriteTaskDescription_InvalidTask_ReturnsNotFound()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/RewriteTaskDescription", new Dictionary<string, string>
        {
            ["taskId"] = "999999",
            ["description"] = "Some description"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not found", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RewriteTaskDescription_Member_OllamaNotConfigured_ReturnsFailure()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();
        var (_, taskId) = await CreateProjectAndTask();

        var response = await PostWithAuth("/Ai/RewriteTaskDescription", new Dictionary<string, string>
        {
            ["taskId"] = taskId.ToString(),
            ["description"] = "A valid description"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not configured", json, StringComparison.OrdinalIgnoreCase);
    }

    // --- PolishComment authorization ---

    [Fact]
    public async Task PolishComment_InvalidTask_ReturnsNotFound()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/PolishComment", new Dictionary<string, string>
        {
            ["taskId"] = "999999",
            ["comment"] = "Some comment"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not found", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PolishComment_Member_OllamaNotConfigured_ReturnsFailure()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();
        var (_, taskId) = await CreateProjectAndTask();

        var response = await PostWithAuth("/Ai/PolishComment", new Dictionary<string, string>
        {
            ["taskId"] = taskId.ToString(),
            ["comment"] = "A valid comment"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not configured", json, StringComparison.OrdinalIgnoreCase);
    }

    // --- ExpandInboxItem authorization ---

    [Fact]
    public async Task ExpandInboxItem_NotOwner_ReturnsAccessDenied()
    {
        // Create inbox item for default user
        await CreateAndLoginUser();
        var inboxItemId = await CreateInboxItem();

        // Try to access as a different user
        var (cookie, token) = await GetAuthenticatedRequestParts("ai-other@test.com");

        var response = await PostWithAuth("/Ai/ExpandInboxItem", new Dictionary<string, string>
        {
            ["inboxItemId"] = inboxItemId.ToString(),
            ["title"] = "Some title"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("Access denied", json);
    }

    [Fact]
    public async Task ExpandInboxItem_InvalidId_ReturnsAccessDenied()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/ExpandInboxItem", new Dictionary<string, string>
        {
            ["inboxItemId"] = "999999",
            ["title"] = "Some title"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("Access denied", json);
    }

    [Fact]
    public async Task ExpandInboxItem_Owner_OllamaNotConfigured_ReturnsFailure()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();
        var inboxItemId = await CreateInboxItem();

        var response = await PostWithAuth("/Ai/ExpandInboxItem", new Dictionary<string, string>
        {
            ["inboxItemId"] = inboxItemId.ToString(),
            ["title"] = "Some title"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not configured", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpandInboxItem_EmptyTitle_ReturnsError()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/ExpandInboxItem", new Dictionary<string, string>
        {
            ["inboxItemId"] = "1",
            ["title"] = ""
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("required", json, StringComparison.OrdinalIgnoreCase);
    }

    // --- ProjectSummary authorization ---

    [Fact]
    public async Task ProjectSummary_NonMember_ReturnsAccessDenied()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/ProjectSummary", new Dictionary<string, string>
        {
            ["projectId"] = "999999",
            ["period"] = "ThisWeek"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("Access denied", json);
    }

    [Fact]
    public async Task ProjectSummary_InvalidPeriod_ReturnsError()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/ProjectSummary", new Dictionary<string, string>
        {
            ["projectId"] = "1",
            ["period"] = "InvalidPeriod"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("Invalid period", json);
    }

    [Fact]
    public async Task ProjectSummary_Member_OllamaNotConfigured_ReturnsFailure()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();
        var (projectId, _) = await CreateProjectAndTask();

        var response = await PostWithAuth("/Ai/ProjectSummary", new Dictionary<string, string>
        {
            ["projectId"] = projectId.ToString(),
            ["period"] = "ThisWeek"
        }, cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not configured", json, StringComparison.OrdinalIgnoreCase);
    }

    // --- PersonalDigest behavior ---

    [Fact]
    public async Task PersonalDigest_OllamaNotConfigured_ReturnsFailure()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/PersonalDigest", new Dictionary<string, string>(), cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not configured", json, StringComparison.OrdinalIgnoreCase);
    }

    // --- ReviewPreparation behavior ---

    [Fact]
    public async Task ReviewPreparation_OllamaNotConfigured_ReturnsFailure()
    {
        var (cookie, token) = await GetAuthenticatedRequestParts();

        var response = await PostWithAuth("/Ai/ReviewPreparation", new Dictionary<string, string>(), cookie, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
        Assert.Contains("not configured", json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
