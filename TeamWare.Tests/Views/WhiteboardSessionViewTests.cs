using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Views;

public class WhiteboardSessionViewTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WhiteboardSessionViewTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [Fact]
    public async Task SessionView_IncludesWhiteboardScriptReferencesAndDataAttributes()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("session-view@test.com", "Owner");
        var whiteboardId = await CreateWhiteboard(ownerId, "View Test Board");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Whiteboard/Session/{whiteboardId}");
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("whiteboard-canvas.js", html);
        Assert.Contains("whiteboard.js", html);
        Assert.Contains("data-whiteboard-session", html);
        Assert.Contains("data-is-owner=\"true\"", html);
        Assert.Contains("data-is-presenter=\"true\"", html);
        Assert.Contains("whiteboard-initial-canvas", html);
    }

    [Fact]
    public async Task SessionView_RendersExistingChatMessagesAndInput()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("session-chat@test.com", "Owner");
        var whiteboardId = await CreateWhiteboard(ownerId, "Chat View Board");
        await CreateChatMessage(whiteboardId, ownerId, "Existing chat message");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Whiteboard/Session/{whiteboardId}");
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("whiteboard-chat-messages", html);
        Assert.Contains("whiteboard-chat-form", html);
        Assert.Contains("whiteboard-chat-input", html);
        Assert.Contains("Existing chat message", html);
        Assert.Contains("Owner", html);
    }

    [Fact]
    public async Task SessionView_OwnerSeesInviteSearchUi()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("session-invite-ui@test.com", "Owner");
        var whiteboardId = await CreateWhiteboard(ownerId, "Invite View Board");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Whiteboard/Session/{whiteboardId}");
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("whiteboard-invite-form", html);
        Assert.Contains("Search Users", html);
        Assert.Contains("Already Invited", html);
    }

    [Fact]
    public async Task SessionView_OwnerSeesPresenterContextActions()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("session-presenter-owner@test.com", "Owner");
        var (viewerId, _) = await CreateAndLoginUser("session-presenter-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboard(ownerId, "Presenter Actions Board");
        await CreateInvitation(whiteboardId, viewerId, ownerId);
        await AddActiveUser(whiteboardId, viewerId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Whiteboard/Session/{whiteboardId}");
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Reclaim Presenter", html);
        Assert.Contains("Make Presenter", html);
        Assert.Contains("Remove User", html);
    }

    [Fact]
    public async Task SessionView_NonOwnerDoesNotSeePresenterContextActions()
    {
        var (ownerId, _) = await CreateAndLoginUser("session-presenter-nonowner-owner@test.com", "Owner");
        var (viewerId, cookie) = await CreateAndLoginUser("session-presenter-nonowner-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboard(ownerId, "Non Owner Presenter Actions Board");
        await CreateInvitation(whiteboardId, viewerId, ownerId);
        await AddActiveUser(whiteboardId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Whiteboard/Session/{whiteboardId}");
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Reclaim Presenter", html);
        Assert.DoesNotContain("Make Presenter", html);
        Assert.DoesNotContain("Remove User", html);
    }

    [Fact]
    public async Task SessionView_SavedBoard_DoesNotShowRemoveUserAction()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("session-saved-owner@test.com", "Owner");
        var (viewerId, _) = await CreateAndLoginUser("session-saved-viewer@test.com", "Viewer");
        var projectId = await CreateProjectWithMembers(ownerId, viewerId);
        var whiteboardId = await CreateWhiteboard(ownerId, "Saved Presenter Actions Board", projectId);
        await AddActiveUser(whiteboardId, viewerId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Whiteboard/Session/{whiteboardId}");
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Make Presenter", html);
        Assert.DoesNotContain("Remove User", html);
    }

    [Fact]
    public async Task SessionView_OwnerSeesProjectAssociationUiWithClearButtonForSavedBoard()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("session-project-owner@test.com", "Owner");
        var (memberId, _) = await CreateAndLoginUser("session-project-member@test.com", "Member");
        var projectId = await CreateProjectWithMembers(ownerId, memberId);
        var whiteboardId = await CreateWhiteboard(ownerId, "Project Association Board", projectId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Whiteboard/Session/{whiteboardId}");
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("whiteboard-project-association", html);
        Assert.Contains("Save Project", html);
        Assert.Contains("Clear Project", html);
        Assert.Contains("Only project members will be able to see this board", html);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email, string displayName)
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
            await userManager.AddToRoleAsync(user, SeedData.UserRoleName);
            existing = user;
        }

        return (existing.Id, await GetLoginCookie(email));
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

    private async Task<int> CreateWhiteboard(string ownerUserId, string title)
    {
        return await CreateWhiteboard(ownerUserId, title, null);
    }

    private async Task<int> CreateWhiteboard(string ownerUserId, string title, int? projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var whiteboard = new Whiteboard
        {
            Title = title,
            OwnerId = ownerUserId,
            ProjectId = projectId,
            CurrentPresenterId = ownerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Whiteboards.Add(whiteboard);
        await context.SaveChangesAsync();
        return whiteboard.Id;
    }

    private async Task CreateChatMessage(int whiteboardId, string userId, string content)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.WhiteboardChatMessages.Add(new WhiteboardChatMessage
        {
            WhiteboardId = whiteboardId,
            UserId = userId,
            Content = content,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    private async Task CreateInvitation(int whiteboardId, string userId, string invitedByUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.WhiteboardInvitations.Add(new WhiteboardInvitation
        {
            WhiteboardId = whiteboardId,
            UserId = userId,
            InvitedByUserId = invitedByUserId,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    private async Task AddActiveUser(int whiteboardId, string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var tracker = scope.ServiceProvider.GetRequiredService<TeamWare.Web.Services.IWhiteboardPresenceTracker>();
        await tracker.AddConnectionAsync(whiteboardId, userId, $"conn-{whiteboardId}-{userId}");
    }

    private async Task<int> CreateProjectWithMembers(string ownerUserId, string memberUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project
        {
            Name = $"Session Test Project {Guid.NewGuid():N}",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.AddRange(
            new ProjectMember
            {
                ProjectId = project.Id,
                UserId = ownerUserId,
                Role = ProjectRole.Owner,
                JoinedAt = DateTime.UtcNow
            },
            new ProjectMember
            {
                ProjectId = project.Id,
                UserId = memberUserId,
                Role = ProjectRole.Member,
                JoinedAt = DateTime.UtcNow
            });

        await context.SaveChangesAsync();
        return project.Id;
    }
}
