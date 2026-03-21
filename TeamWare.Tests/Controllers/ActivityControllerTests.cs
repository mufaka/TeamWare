using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class ActivityControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ActivityControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(
        string email = "acttest@test.com", string displayName = "Activity Test User")
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

    private async Task CreateActivityData(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project { Name = "Activity Test Project" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner
        });
        await context.SaveChangesAsync();

        var task = new TaskItem
        {
            Title = "Activity Test Task",
            ProjectId = project.Id,
            CreatedByUserId = userId,
            Status = TaskItemStatus.ToDo
        };
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        context.ActivityLogEntries.Add(new ActivityLogEntry
        {
            TaskItemId = task.Id,
            ProjectId = project.Id,
            UserId = userId,
            ChangeType = ActivityChangeType.Created,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    // --- Authentication ---

    [Fact]
    public async Task GlobalFeed_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Activity/GlobalFeed");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- GlobalFeed ---

    [Fact]
    public async Task GlobalFeed_Authenticated_ReturnsSuccess()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Activity/GlobalFeed");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GlobalFeed_WithActivity_ReturnsContent()
    {
        var (userId, cookie) = await CreateAndLoginUser("actfeed@test.com", "Feed User");
        await CreateActivityData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Activity/GlobalFeed");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Activity Test Task", html);
    }

    [Fact]
    public async Task GlobalFeed_NonMemberUser_ShowsMaskedOrEmptyEntries()
    {
        var (_, cookie) = await CreateAndLoginUser("actempty@test.com", "Empty Feed User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Activity/GlobalFeed");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // User is not a member of any project, so they should either see
        // "No recent activity" or masked entries (no full task details visible)
        Assert.DoesNotContain("Activity Test Task", html);
    }

    [Fact]
    public async Task GlobalFeed_MaskedEntries_DoNotShowTaskDetails()
    {
        var (userId, cookie) = await CreateAndLoginUser("actmasked@test.com", "Masked Feed User");

        // Create a project and activity where the user is NOT a member
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var otherUser = new ApplicationUser
            {
                UserName = "other-act@test.com",
                Email = "other-act@test.com",
                DisplayName = "Other User"
            };
            context.Users.Add(otherUser);
            await context.SaveChangesAsync();

            var project = new Project { Name = "Secret Activity Project" };
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var task = new TaskItem
            {
                Title = "Top Secret Task Name",
                ProjectId = project.Id,
                CreatedByUserId = otherUser.Id,
                Status = TaskItemStatus.ToDo
            };
            context.TaskItems.Add(task);
            await context.SaveChangesAsync();

            context.ActivityLogEntries.Add(new ActivityLogEntry
            {
                TaskItemId = task.Id,
                ProjectId = project.Id,
                UserId = otherUser.Id,
                ChangeType = ActivityChangeType.Created,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "/Activity/GlobalFeed");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Top Secret Task Name", html);
        Assert.Contains("A task was created", html);
    }

    // --- Dashboard Integration ---

    [Fact]
    public async Task Dashboard_ContainsActivityFeedSection()
    {
        var (_, cookie) = await CreateAndLoginUser("actdash@test.com", "Dashboard User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Recent Activity", html);
        Assert.Contains("/Activity/GlobalFeed", html);
    }

    // --- Directory Presence Indicators ---

    [Fact]
    public async Task Directory_ContainsPresenceIndicators()
    {
        var (_, cookie) = await CreateAndLoginUser("actdir@test.com", "Directory Presence User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-presence-user", html);
    }

    // --- Profile Presence and Last Active ---

    [Fact]
    public async Task Profile_ContainsPresenceIndicator()
    {
        var (userId, cookie) = await CreateAndLoginUser("actprofile@test.com", "Profile Presence User");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Directory/Profile/{userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-presence-user", html);
    }

    [Fact]
    public async Task Profile_ShowsLastActiveOrNeverActive()
    {
        var (userId, cookie) = await CreateAndLoginUser("actlast@test.com", "Last Active User");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Directory/Profile/{userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // User has not connected via SignalR, so they should show "Never active" or "Last active"
        Assert.True(html.Contains("Never active") || html.Contains("Last active") || html.Contains("Online"));
    }

    [Fact]
    public async Task GlobalFeed_WithCountParam_ReturnsSuccess()
    {
        var (_, cookie) = await CreateAndLoginUser("actcount@test.com", "Count User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Activity/GlobalFeed?count=5");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
