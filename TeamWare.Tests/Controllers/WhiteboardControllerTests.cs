using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class WhiteboardControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WhiteboardControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [Fact]
    public async Task Index_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Whiteboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Index_ShowsBoardsTheUserIsInvitedTo()
    {
        var (ownerId, _) = await CreateAndLoginUser("whiteboard-owner@test.com", "Owner");
        var (inviteeId, cookie) = await CreateAndLoginUser("whiteboard-invitee@test.com", "Invitee");
        var whiteboardId = await CreateWhiteboard(ownerId, "Invited Temporary Board");
        await CreateInvitation(whiteboardId, inviteeId, ownerId);

        var response = await SendAuthenticatedGetAsync("/Whiteboard", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invited Temporary Board", content);
    }

    [Fact]
    public async Task Index_ShowsBoardsAccessibleViaProjectMembership()
    {
        var (ownerId, _) = await CreateAndLoginUser("saved-owner@test.com", "Owner");
        var (memberId, cookie) = await CreateAndLoginUser("saved-member@test.com", "Member");
        var projectId = await CreateProjectWithMembers(ownerId, memberId);
        await CreateWhiteboard(ownerId, "Saved Project Board", projectId);

        var response = await SendAuthenticatedGetAsync("/Whiteboard", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Saved Project Board", content);
    }

    [Fact]
    public async Task Index_SiteAdminSeesAllBoards()
    {
        var (ownerId, _) = await CreateAndLoginUser("admin-owner@test.com", "Owner");
        var (_, cookie) = await CreateAndLoginUser("site-admin@test.com", "Site Admin", true);
        await CreateWhiteboard(ownerId, "Admin Visible Board One");
        await CreateWhiteboard(ownerId, "Admin Visible Board Two");

        var response = await SendAuthenticatedGetAsync("/Whiteboard", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin Visible Board One", content);
        Assert.Contains("Admin Visible Board Two", content);
    }

    [Fact]
    public async Task Create_Get_Authenticated_ReturnsForm()
    {
        var (_, cookie) = await CreateAndLoginUser("create-whiteboard@test.com", "Creator");

        var response = await SendAuthenticatedGetAsync("/Whiteboard/Create", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Create Whiteboard", content);
    }

    [Fact]
    public async Task Create_Post_WithValidTitle_RedirectsToSession()
    {
        var (userId, cookie) = await CreateAndLoginUser("valid-whiteboard@test.com", "Creator");
        var (token, cookies) = await GetAntiForgeryFromPage("/Whiteboard/Create", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Create");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "Architecture Review",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Whiteboard/Session", response.Headers.Location?.ToString());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whiteboard = context.Whiteboards.Single(w => w.Title == "Architecture Review");
        Assert.Equal(userId, whiteboard.OwnerId);
        Assert.Equal(userId, whiteboard.CurrentPresenterId);
    }

    [Fact]
    public async Task Create_Post_WithEmptyTitle_FailsValidation()
    {
        var (_, cookie) = await CreateAndLoginUser("empty-whiteboard@test.com", "Creator");
        var (token, cookies) = await GetAntiForgeryFromPage("/Whiteboard/Create", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Create");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = string.Empty,
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("field is required", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_Post_WithTitleExceeding200Characters_FailsValidation()
    {
        var (_, cookie) = await CreateAndLoginUser("long-whiteboard@test.com", "Creator");
        var (token, cookies) = await GetAntiForgeryFromPage("/Whiteboard/Create", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Create");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = new string('a', 201),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("at most 200 characters long", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_AuthorizedUser_LoadsSessionPage()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("session-owner@test.com", "Owner");
        var whiteboardId = await CreateWhiteboard(ownerId, "Session Board");

        var response = await SendAuthenticatedGetAsync($"/Whiteboard/Session/{whiteboardId}", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Session Board", content);
        Assert.Contains("data-whiteboard-id", content);
        Assert.Contains("whiteboard-canvas", content);
    }

    [Fact]
    public async Task Session_UnauthorizedUser_ReturnsForbidden()
    {
        var (ownerId, _) = await CreateAndLoginUser("forbid-owner@test.com", "Owner");
        var (_, cookie) = await CreateAndLoginUser("forbid-outsider@test.com", "Outsider");
        var whiteboardId = await CreateWhiteboard(ownerId, "Protected Session Board");

        var response = await SendAuthenticatedGetAsync($"/Whiteboard/Session/{whiteboardId}", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SaveToProject_AsOwnerAndProjectMember_UpdatesProjectAssociation()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("save-project-owner@test.com", "Owner");
        var (memberId, _) = await CreateAndLoginUser("save-project-member@test.com", "Member");
        var projectId = await CreateProjectWithMembers(ownerId, memberId);
        var whiteboardId = await CreateWhiteboard(ownerId, "Save Project Board");
        var (token, cookies) = await GetAntiForgeryFromPage($"/Whiteboard/Session/{whiteboardId}", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/SaveToProject");
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["whiteboardId"] = whiteboardId.ToString(),
            ["projectId"] = projectId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Whiteboard saved to project", html);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(projectId, (await context.Whiteboards.FindAsync(whiteboardId))!.ProjectId);
    }

    [Fact]
    public async Task SaveToProject_AsOwnerButNotProjectMember_ReturnsUnprocessableEntity()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("save-project-restricted-owner@test.com", "Owner");
        var (projectOwnerId, _) = await CreateAndLoginUser("save-project-restricted-project-owner@test.com", "Project Owner");
        var (projectMemberId, _) = await CreateAndLoginUser("save-project-restricted-member@test.com", "Member");
        var projectId = await CreateProjectWithMembers(projectOwnerId, projectMemberId);
        var whiteboardId = await CreateWhiteboard(ownerId, "Restricted Save Project Board");
        var (token, cookies) = await GetAntiForgeryFromPage($"/Whiteboard/Session/{whiteboardId}", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/SaveToProject");
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["whiteboardId"] = whiteboardId.ToString(),
            ["projectId"] = projectId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("project members", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveToProject_AsNonOwner_ReturnsForbidden()
    {
        var (ownerId, _) = await CreateAndLoginUser("save-project-protected-owner@test.com", "Owner");
        var (viewerId, cookie) = await CreateAndLoginUser("save-project-protected-viewer@test.com", "Viewer");
        var projectId = await CreateProjectWithMembers(ownerId, viewerId);
        var whiteboardId = await CreateWhiteboard(ownerId, "Protected Save Project Board");
        await CreateInvitation(whiteboardId, viewerId, ownerId);
        var (token, cookies) = await GetAntiForgeryFromPage($"/Whiteboard/Session/{whiteboardId}", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/SaveToProject");
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["whiteboardId"] = whiteboardId.ToString(),
            ["projectId"] = projectId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ClearProject_AsOwner_RestoresTemporaryStatus()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("clear-project-owner@test.com", "Owner");
        var (memberId, _) = await CreateAndLoginUser("clear-project-member@test.com", "Member");
        var projectId = await CreateProjectWithMembers(ownerId, memberId);
        var whiteboardId = await CreateWhiteboard(ownerId, "Clear Project Board", projectId);
        var (token, cookies) = await GetAntiForgeryFromPage($"/Whiteboard/Session/{whiteboardId}", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/ClearProject");
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["whiteboardId"] = whiteboardId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("temporary status", html, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null((await context.Whiteboards.FindAsync(whiteboardId))!.ProjectId);
    }

    [Fact]
    public async Task InviteForm_AsOwner_ReturnsInviteUi()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("invite-form-owner@test.com", "Owner");
        var whiteboardId = await CreateWhiteboard(ownerId, "Invite UI Board");

        var response = await SendAuthenticatedGetAsync($"/Whiteboard/InviteForm?whiteboardId={whiteboardId}", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Search Users", content);
        Assert.Contains("Already Invited", content);
    }

    [Fact]
    public async Task InviteForm_AsNonOwner_ReturnsForbidden()
    {
        var (ownerId, _) = await CreateAndLoginUser("invite-form-protected-owner@test.com", "Owner");
        var (_, cookie) = await CreateAndLoginUser("invite-form-outsider@test.com", "Outsider");
        var whiteboardId = await CreateWhiteboard(ownerId, "Protected Invite UI Board");

        var response = await SendAuthenticatedGetAsync($"/Whiteboard/InviteForm?whiteboardId={whiteboardId}", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invite_AsOwner_CreatesInvitation()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("invite-post-owner@test.com", "Owner");
        var (inviteeId, _) = await CreateAndLoginUser("invite-post-user@test.com", "Invitee");
        var whiteboardId = await CreateWhiteboard(ownerId, "Invite Post Board");
        var (token, cookies) = await GetAntiForgeryFromPage($"/Whiteboard/Session/{whiteboardId}", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Invite");
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["whiteboardId"] = whiteboardId.ToString(),
            ["userIds"] = inviteeId,
            ["query"] = "Invitee",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invitation sent successfully", html);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull(await context.WhiteboardInvitations.SingleOrDefaultAsync(i => i.WhiteboardId == whiteboardId && i.UserId == inviteeId));
        Assert.NotNull(await context.Notifications.SingleOrDefaultAsync(n => n.ReferenceId == whiteboardId && n.Type == NotificationType.WhiteboardInvitation));
    }

    [Fact]
    public async Task Invite_AsNonOwner_ReturnsUnprocessableEntity()
    {
        var (ownerId, _) = await CreateAndLoginUser("invite-post-protected-owner@test.com", "Owner");
        var (inviteeId, _) = await CreateAndLoginUser("invite-post-protected-user@test.com", "Invitee");
        var (_, cookie) = await CreateAndLoginUser("invite-post-outsider@test.com", "Outsider");
        var whiteboardId = await CreateWhiteboard(ownerId, "Protected Invite Post Board");
        var (token, cookies) = await GetAntiForgeryFromPage("/Whiteboard/Create", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Invite");
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["whiteboardId"] = whiteboardId.ToString(),
            ["userIds"] = inviteeId,
            ["query"] = "Invitee",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invite_DuplicateInvitation_IsHandledGracefully()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("invite-duplicate-owner@test.com", "Owner");
        var (inviteeId, _) = await CreateAndLoginUser("invite-duplicate-user@test.com", "Invitee");
        var whiteboardId = await CreateWhiteboard(ownerId, "Duplicate Invite Board");
        await CreateInvitation(whiteboardId, inviteeId, ownerId);
        var (token, cookies) = await GetAntiForgeryFromPage($"/Whiteboard/Session/{whiteboardId}", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Invite");
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("HX-Request", "true");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["whiteboardId"] = whiteboardId.ToString(),
            ["userIds"] = inviteeId,
            ["query"] = "Invitee",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("already invited", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_DeletedBoard_RedirectsToLandingPage()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("deleted-session@test.com", "Owner");
        var whiteboardId = await CreateWhiteboard(ownerId, "Deleted Board");
        await DeleteWhiteboard(whiteboardId);

        var response = await SendAuthenticatedGetAsync($"/Whiteboard/Session/{whiteboardId}", cookie);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Whiteboard", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Session_DeletedBoardInvitation_IsCleanedUp()
    {
        var (ownerId, _) = await CreateAndLoginUser("deleted-invite-owner@test.com", "Owner");
        var (inviteeId, cookie) = await CreateAndLoginUser("deleted-invitee@test.com", "Invitee");
        var whiteboardId = await CreateWhiteboard(ownerId, "Deleted Invited Board");
        await CreateInvitation(whiteboardId, inviteeId, ownerId);
        await DeleteWhiteboard(whiteboardId);

        var response = await SendAuthenticatedGetAsync($"/Whiteboard/Session/{whiteboardId}", cookie);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(context.WhiteboardInvitations.SingleOrDefault(i => i.WhiteboardId == whiteboardId && i.UserId == inviteeId));
    }

    [Fact]
    public async Task Session_OwnerSeesOwnerOnlyControls()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("owner-controls@test.com", "Owner");
        var (memberId, _) = await CreateAndLoginUser("owner-controls-member@test.com", "Member");
        var projectId = await CreateProjectWithMembers(ownerId, memberId);
        var whiteboardId = await CreateWhiteboard(ownerId, "Owner Controls Board", projectId);

        var response = await SendAuthenticatedGetAsync($"/Whiteboard/Session/{whiteboardId}", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invite Users", content);
        Assert.Contains("Save to Project", content);
        Assert.Contains("Delete", content);
        Assert.Contains("Presenter:", content);
    }

    [Fact]
    public async Task Session_NonOwnerViewer_DoesNotSeeOwnerOnlyControls()
    {
        var (ownerId, _) = await CreateAndLoginUser("viewer-owner@test.com", "Owner");
        var (viewerId, cookie) = await CreateAndLoginUser("viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboard(ownerId, "Viewer Board");
        await CreateInvitation(whiteboardId, viewerId, ownerId);

        var response = await SendAuthenticatedGetAsync($"/Whiteboard/Session/{whiteboardId}", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Invite Users", content);
        Assert.DoesNotContain("Save to Project", content);
    }

    [Fact]
    public async Task Delete_Post_AsOwner_DeletesWhiteboard()
    {
        var (ownerId, cookie) = await CreateAndLoginUser("delete-owner-phase56@test.com", "Owner");
        var whiteboardId = await CreateWhiteboard(ownerId, "Delete Me");
        var (token, cookies) = await GetAntiForgeryFromPage($"/Whiteboard/Session/{whiteboardId}", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Delete");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = whiteboardId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Whiteboard", response.Headers.Location?.ToString());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(context.Whiteboards.SingleOrDefault(w => w.Id == whiteboardId));
    }

    [Fact]
    public async Task Delete_Post_AsSiteAdmin_DeletesWhiteboard()
    {
        var (ownerId, _) = await CreateAndLoginUser("delete-admin-owner-phase56@test.com", "Owner");
        var (_, cookie) = await CreateAndLoginUser("delete-admin-phase56@test.com", "Admin", true);
        var whiteboardId = await CreateWhiteboard(ownerId, "Delete As Admin");
        var (token, cookies) = await GetAntiForgeryFromPage($"/Whiteboard/Session/{whiteboardId}", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Delete");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = whiteboardId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Whiteboard", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Delete_Post_AsNonOwnerNonAdmin_ReturnsForbidden()
    {
        var (ownerId, _) = await CreateAndLoginUser("delete-protected-owner@test.com", "Owner");
        var (_, cookie) = await CreateAndLoginUser("delete-protected-user@test.com", "User");
        var whiteboardId = await CreateWhiteboard(ownerId, "Protected Delete Board");
        var (token, cookies) = await GetAntiForgeryFromPage("/Whiteboard/Create", cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Whiteboard/Delete");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = whiteboardId.ToString(),
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email, string displayName, bool isAdmin = false)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.EnsureCreatedAsync();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            if (isAdmin && !await userManager.IsInRoleAsync(existing, SeedData.AdminRoleName))
            {
                await userManager.AddToRoleAsync(existing, SeedData.AdminRoleName);
            }

            return (existing.Id, await GetLoginCookie(email));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };

        await userManager.CreateAsync(user, "TestPass1!");
        await userManager.AddToRoleAsync(user, SeedData.UserRoleName);
        if (isAdmin)
        {
            await userManager.AddToRoleAsync(user, SeedData.AdminRoleName);
        }

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

    private async Task<(string Token, string Cookies)> GetAntiForgeryFromPage(string url, string authCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", authCookie);
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        var token = ExtractAntiForgeryToken(html);
        var allCookies = new List<string> { authCookie };
        if (response.Headers.TryGetValues("Set-Cookie", out var responseCookies))
        {
            allCookies.AddRange(responseCookies);
        }

        return (token, string.Join("; ", allCookies));
    }

    private async Task<HttpResponseMessage> SendAuthenticatedGetAsync(string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(request);
    }

    private async Task<int> CreateProjectWithMembers(string ownerUserId, string memberUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project
        {
            Name = $"Whiteboard Project {Guid.NewGuid():N}",
            Description = "Project used by whiteboard controller tests",
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

    private async Task<int> CreateWhiteboard(string ownerUserId, string title, int? projectId = null)
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

    private async Task DeleteWhiteboard(int whiteboardId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whiteboard = context.Whiteboards.Single(w => w.Id == whiteboardId);
        context.Whiteboards.Remove(whiteboard);
        await context.SaveChangesAsync();
    }
}
