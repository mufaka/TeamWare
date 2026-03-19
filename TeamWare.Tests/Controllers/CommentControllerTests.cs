using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class CommentControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CommentControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "comment-test@test.com")
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
                DisplayName = "Comment Test User"
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

    private async Task<(int ProjectId, int TaskId)> CreateProjectAndTask(string loginCookie)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync("comment-test@test.com");

        var project = new Project
        {
            Name = $"Comment Test Project {Guid.NewGuid():N}",
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
            Title = "Comment Test Task",
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

    private async Task<int> CreateComment(int taskItemId, string userId, string content = "Test comment")
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var comment = new Comment
        {
            TaskItemId = taskItemId,
            AuthorId = userId,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Comments.Add(comment);
        await context.SaveChangesAsync();
        return comment.Id;
    }

    // --- Add Comment ---

    [Fact]
    public async Task AddComment_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Comment/Add");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TaskItemId"] = "1",
            ["Content"] = "Test"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AddComment_Authenticated_RedirectsToTaskDetails()
    {
        var loginCookie = await CreateAndLoginUser();
        var (_, taskId) = await CreateProjectAndTask(loginCookie);

        // Get a page with anti-forgery token
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var responseCookies = getResponse.Headers.GetValues("Set-Cookie");
        var allCookies = loginCookie + "; " + string.Join("; ", responseCookies);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Comment/Add");
        request.Headers.Add("Cookie", allCookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TaskItemId"] = taskId.ToString(),
            ["Content"] = "Integration test comment",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Task/Details/{taskId}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AddComment_HtmxRequest_ReturnsPartial()
    {
        var loginCookie = await CreateAndLoginUser();
        var (_, taskId) = await CreateProjectAndTask(loginCookie);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var responseCookies = getResponse.Headers.GetValues("Set-Cookie");
        var allCookies = loginCookie + "; " + string.Join("; ", responseCookies);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Comment/Add");
        request.Headers.Add("Cookie", allCookies);
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TaskItemId"] = taskId.ToString(),
            ["Content"] = "HTMX comment",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("HTMX comment", content);
    }

    // --- Delete Comment ---

    [Fact]
    public async Task DeleteComment_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Comment/Delete");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["commentId"] = "1",
            ["taskItemId"] = "1"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- Task Details shows comments section ---

    [Fact]
    public async Task TaskDetails_ShowsCommentsSection()
    {
        var loginCookie = await CreateAndLoginUser();
        var (_, taskId) = await CreateProjectAndTask(loginCookie);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Comments", content);
        Assert.Contains("Add Comment", content);
    }

    [Fact]
    public async Task TaskDetails_ShowsExistingComments()
    {
        var loginCookie = await CreateAndLoginUser();
        var (_, taskId) = await CreateProjectAndTask(loginCookie);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("comment-test@test.com");

        await CreateComment(taskId, user!.Id, "Visible comment text");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Visible comment text", content);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
