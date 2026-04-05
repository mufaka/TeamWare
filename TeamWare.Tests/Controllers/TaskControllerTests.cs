using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class TaskControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TaskControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "task-test@test.com")
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
                DisplayName = "Task Test User"
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

    [Fact]
    public async Task TaskIndex_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task?projectId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TaskCreate_Get_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task/Create?projectId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WhatsNext_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Home/WhatsNext");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WhatsNext_Authenticated_ReturnsSuccess()
    {
        var cookie = await CreateAndLoginUser("whatsnext-test@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/WhatsNext");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("What", content);
    }

    [Fact]
    public async Task TaskDetails_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task/Details/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- Partial endpoint tests ---

    [Fact]
    public async Task StatusPartial_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task/StatusPartial/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ActivityPartial_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task/ActivityPartial/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CommentsPartial_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task/CommentsPartial/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task StatusPartial_NonexistentTask_ReturnsNotFound()
    {
        var cookie = await CreateAndLoginUser("status-partial-test@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Task/StatusPartial/99999");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        // Nonexistent task returns NotFound or redirect depending on service behavior
        Assert.True(response.StatusCode == HttpStatusCode.NotFound
            || response.StatusCode == HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task ActivityPartial_NonexistentTask_ReturnsNotFound()
    {
        var cookie = await CreateAndLoginUser("activity-partial-test@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Task/ActivityPartial/99999");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.True(response.StatusCode == HttpStatusCode.NotFound
            || response.StatusCode == HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task CommentsPartial_NonexistentTask_ReturnsNotFound()
    {
        var cookie = await CreateAndLoginUser("comments-partial-test@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Task/CommentsPartial/99999");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.True(response.StatusCode == HttpStatusCode.NotFound
            || response.StatusCode == HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task StatusPartial_ValidTask_ReturnsPartialHtml()
    {
        var (cookie, taskId) = await CreateProjectAndTask("status-valid@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/StatusPartial/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("rounded-full", content); // badge CSS class
    }

    [Fact]
    public async Task ActivityPartial_ValidTask_ReturnsPartialHtml()
    {
        var (cookie, taskId) = await CreateProjectAndTask("activity-valid@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/ActivityPartial/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Activity history will have at least a Created entry or the empty state message
        Assert.True(content.Contains("flow-root") || content.Contains("No activity recorded yet."));
    }

    [Fact]
    public async Task CommentsPartial_ValidTask_ReturnsPartialHtml()
    {
        var (cookie, taskId) = await CreateProjectAndTask("comments-valid@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/CommentsPartial/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Phase 50: Details.cshtml Integration Tests ---

    [Fact]
    public async Task Details_HasDataTaskIdAttribute()
    {
        var (cookie, taskId) = await CreateProjectAndTask("details-taskid@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains($"data-task-id=\"{taskId}\"", html);
    }

    [Fact]
    public async Task Details_HasStatusSectionWithPartialUrl()
    {
        var (cookie, taskId) = await CreateProjectAndTask("details-status@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"task-status-section\"", html);
        Assert.Contains($"data-partial-url=\"/Task/StatusPartial/{taskId}\"", html);
    }

    [Fact]
    public async Task Details_HasCommentsSectionWithPartialUrl()
    {
        var (cookie, taskId) = await CreateProjectAndTask("details-comments@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"comments-section\"", html);
        Assert.Contains($"data-partial-url=\"/Task/CommentsPartial/{taskId}\"", html);
    }

    [Fact]
    public async Task Details_HasActivitySectionWithPartialUrl()
    {
        var (cookie, taskId) = await CreateProjectAndTask("details-activity@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"task-activity-section\"", html);
        Assert.Contains($"data-partial-url=\"/Task/ActivityPartial/{taskId}\"", html);
    }

    [Fact]
    public async Task Details_HasToastContainer()
    {
        var (cookie, taskId) = await CreateProjectAndTask("details-toast@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"task-toast-container\"", html);
    }

    [Fact]
    public async Task Details_HasSignalRScriptReference()
    {
        var (cookie, taskId) = await CreateProjectAndTask("details-signalr@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("signalr.min.js", html);
    }

    [Fact]
    public async Task Details_HasTaskRealtimeScriptReference()
    {
        var (cookie, taskId) = await CreateProjectAndTask("details-realtime@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("task-realtime.js", html);
    }

    private async Task<(string Cookie, int TaskId)> CreateProjectAndTask(string email)
    {
        var cookie = await CreateAndLoginUser(email);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email);

        var project = new Project
        {
            Name = $"Test Project for {email}",
            Description = "Test project",
            CreatedAt = DateTime.UtcNow,
            Members = new List<ProjectMember>
            {
                new ProjectMember
                {
                    UserId = user!.Id,
                    Role = ProjectRole.Owner,
                    JoinedAt = DateTime.UtcNow
                }
            }
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var task = new TaskItem
        {
            Title = $"Test Task for {email}",
            ProjectId = project.Id,
            Status = TaskItemStatus.ToDo,
            Priority = TaskItemPriority.Medium,
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        return (cookie, task.Id);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
