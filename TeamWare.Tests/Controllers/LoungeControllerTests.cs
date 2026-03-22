using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class LoungeControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LoungeControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "lounge-test@test.com", string displayName = "Lounge Test User")
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
                DisplayName = displayName
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

    private async Task<(int ProjectId, string UserId)> CreateProjectWithMember(string email = "lounge-test@test.com")
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email);

        var project = new Project
        {
            Name = $"Lounge Test Project {Guid.NewGuid():N}",
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

        return (project.Id, user.Id);
    }

    private async Task<int> CreateLoungeMessage(int? projectId, string userId, string content = "Test message")
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = new LoungeMessage
        {
            ProjectId = projectId,
            UserId = userId,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
        context.LoungeMessages.Add(message);
        await context.SaveChangesAsync();

        return message.Id;
    }

    // --- Room Authorization Tests ---

    [Fact]
    public async Task Room_General_AuthenticatedUser_ReturnsOk()
    {
        var loginCookie = await CreateAndLoginUser("lounge-room-gen@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Room");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("#general", content);
    }

    [Fact]
    public async Task Room_General_UnauthenticatedUser_Redirects()
    {
        var response = await _client.GetAsync("/Lounge/Room");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Room_ProjectRoom_MemberCanAccess()
    {
        var loginCookie = await CreateAndLoginUser("lounge-room-member@test.com");
        var (projectId, _) = await CreateProjectWithMember("lounge-room-member@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Room?projectId={projectId}");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Room_ProjectRoom_NonMemberGetsForbidden()
    {
        var loginCookie = await CreateAndLoginUser("lounge-room-nonmember@test.com");

        // Create a project that this user is NOT a member of
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Create another user who is the project owner
        var ownerEmail = $"lounge-owner-{Guid.NewGuid():N}@test.com";
        var owner = new ApplicationUser
        {
            UserName = ownerEmail,
            Email = ownerEmail,
            DisplayName = "Project Owner"
        };
        await userManager.CreateAsync(owner, "TestPass1!");

        var project = new Project
        {
            Name = $"NonMember Test {Guid.NewGuid():N}",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = owner.Id,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Room?projectId={project.Id}");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        // Forbid() with Identity returns a redirect to AccessDenied
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Room_NonexistentProject_ReturnsRedirectToAccessDenied()
    {
        var loginCookie = await CreateAndLoginUser("lounge-room-notfound@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Room?projectId=99999");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        // Non-member gets Forbid() which redirects to AccessDenied
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
    }

    // --- Messages Pagination Tests ---

    [Fact]
    public async Task Messages_ReturnsPartialView()
    {
        var loginCookie = await CreateAndLoginUser("lounge-msgs@test.com");
        var (projectId, userId) = await CreateProjectWithMember("lounge-msgs@test.com");

        // Create some messages
        for (int i = 0; i < 5; i++)
        {
            await CreateLoungeMessage(projectId, userId, $"Message {i}");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Messages?projectId={projectId}&count=50");
        request.Headers.Add("Cookie", loginCookie);
        request.Headers.Add("HX-Request", "true");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Message 0", content);
        Assert.Contains("Message 4", content);
    }

    [Fact]
    public async Task Messages_General_Authenticated_ReturnsOk()
    {
        var loginCookie = await CreateAndLoginUser("lounge-msgs-gen@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Messages?count=50");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- PinnedMessages Tests ---

    [Fact]
    public async Task PinnedMessages_ReturnsPartialView()
    {
        var loginCookie = await CreateAndLoginUser("lounge-pinned@test.com");
        var (projectId, userId) = await CreateProjectWithMember("lounge-pinned@test.com");

        // Create a pinned message
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var msg = new LoungeMessage
            {
                ProjectId = projectId,
                UserId = userId,
                Content = "Pinned test message",
                CreatedAt = DateTime.UtcNow,
                IsPinned = true,
                PinnedByUserId = userId,
                PinnedAt = DateTime.UtcNow
            };
            context.LoungeMessages.Add(msg);
            await context.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/PinnedMessages?projectId={projectId}");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Pinned test message", content);
    }

    // --- CreateTaskFromMessage Tests ---

    [Fact]
    public async Task CreateTaskFromMessage_ProjectMember_CreatesTask()
    {
        var loginCookie = await CreateAndLoginUser("lounge-task@test.com");
        var (projectId, userId) = await CreateProjectWithMember("lounge-task@test.com");

        var messageId = await CreateLoungeMessage(projectId, userId, "Convert this to a task");

        // Get antiforgery token
        var roomRequest = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Room?projectId={projectId}");
        roomRequest.Headers.Add("Cookie", loginCookie);
        var roomResponse = await _client.SendAsync(roomRequest);
        var roomContent = await roomResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(roomContent);
        var allCookies = new List<string> { loginCookie };
        if (roomResponse.Headers.TryGetValues("Set-Cookie", out var roomCookies))
        {
            allCookies.AddRange(roomCookies);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/Lounge/CreateTaskFromMessage");
        request.Headers.Add("Cookie", string.Join("; ", allCookies));
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["messageId"] = messageId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Task #", content);
        Assert.Contains("created", content);
    }

    [Fact]
    public async Task CreateTaskFromMessage_GeneralRoom_Fails()
    {
        var loginCookie = await CreateAndLoginUser("lounge-task-gen@test.com");

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("lounge-task-gen@test.com");

        var messageId = await CreateLoungeMessage(null, user!.Id, "General room message");

        // Get antiforgery token
        var roomRequest = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Room");
        roomRequest.Headers.Add("Cookie", loginCookie);
        var roomResponse = await _client.SendAsync(roomRequest);
        var roomContent = await roomResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(roomContent);
        var allCookies = new List<string> { loginCookie };
        if (roomResponse.Headers.TryGetValues("Set-Cookie", out var roomCookies))
        {
            allCookies.AddRange(roomCookies);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/Lounge/CreateTaskFromMessage");
        request.Headers.Add("Cookie", string.Join("; ", allCookies));
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["messageId"] = messageId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        // Should fail because #general messages can't create tasks
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity || response.StatusCode == HttpStatusCode.BadRequest);
    }

    // --- Pin/Unpin Tests ---

    [Fact]
    public async Task PinMessage_ProjectAdmin_CanPin()
    {
        var loginCookie = await CreateAndLoginUser("lounge-pin-admin@test.com");
        var (projectId, userId) = await CreateProjectWithMember("lounge-pin-admin@test.com");

        var messageId = await CreateLoungeMessage(projectId, userId, "Pin this message");

        // Get antiforgery token
        var roomRequest = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Room?projectId={projectId}");
        roomRequest.Headers.Add("Cookie", loginCookie);
        var roomResponse = await _client.SendAsync(roomRequest);
        var roomContent = await roomResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(roomContent);
        var allCookies = new List<string> { loginCookie };
        if (roomResponse.Headers.TryGetValues("Set-Cookie", out var roomCookies))
        {
            allCookies.AddRange(roomCookies);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/Lounge/PinMessage");
        request.Headers.Add("Cookie", string.Join("; ", allCookies));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["messageId"] = messageId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        // Owner can pin (redirects or returns partial)
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.Redirect);
    }

    // --- MemberSearch Tests ---

    [Fact]
    public async Task MemberSearch_ReturnsMembers()
    {
        var loginCookie = await CreateAndLoginUser("lounge-search@test.com", "Search Test User");
        var (projectId, _) = await CreateProjectWithMember("lounge-search@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/MemberSearch?projectId={projectId}&term=Search");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Search Test User", content);
    }

    [Fact]
    public async Task MemberSearch_EmptyTerm_ReturnsEmpty()
    {
        var loginCookie = await CreateAndLoginUser("lounge-search-empty@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/MemberSearch?term=");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", content);
    }

    // --- Sidebar ViewComponent Tests ---

    [Fact]
    public async Task Sidebar_ContainsGeneralLoungeLink()
    {
        var loginCookie = await CreateAndLoginUser("lounge-sidebar@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("#general", content);
        Assert.Contains("Lounge", content);
    }

    [Fact]
    public async Task Room_ContainsLoungeJavaScript()
    {
        var loginCookie = await CreateAndLoginUser("lounge-js@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Room");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("lounge.js", content);
        Assert.Contains("lounge-room", content);
        Assert.Contains("message-input", content);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
