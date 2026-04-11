using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TeamWare.Web.Data;
using TeamWare.Web.Hubs;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Infrastructure;

public class WhiteboardHubTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WhiteboardHubTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [Fact]
    public void WhiteboardHub_InheritsFromHub()
    {
        Assert.True(typeof(Hub).IsAssignableFrom(typeof(WhiteboardHub)));
    }

    [Fact]
    public void WhiteboardHub_HasAuthorizeAttribute()
    {
        var authorizeAttributes = typeof(WhiteboardHub).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);
        Assert.NotEmpty(authorizeAttributes);
    }

    [Fact]
    public void WhiteboardHub_GetGroupName_ReturnsExpectedFormat()
    {
        Assert.Equal("whiteboard-42", WhiteboardHub.GetGroupName(42));
    }

    [Fact]
    public void WhiteboardHub_IsRegistered_InDependencyInjection()
    {
        using var scope = _factory.Services.CreateScope();
        var hubContext = scope.ServiceProvider.GetService<IHubContext<WhiteboardHub>>();
        var tracker = scope.ServiceProvider.GetService<IWhiteboardPresenceTracker>();

        Assert.NotNull(hubContext);
        Assert.NotNull(tracker);
    }

    [Fact]
    public async Task WhiteboardHub_Endpoint_Unauthenticated_RejectsRequest()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var response = await _client.PostAsync("/hubs/whiteboard/negotiate?negotiateVersion=1", null);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WhiteboardHub_NegotiateEndpoint_Authenticated_ReturnsOk()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/whiteboard/negotiate?negotiateVersion=1");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task JoinBoard_SucceedsForInvitedUser_OnTemporaryBoard()
    {
        var owner = await CreateUserAsync("owner-temp@test.com", "Owner");
        var invitee = await CreateUserAsync("invitee-temp@test.com", "Invitee");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Temporary Board");
        await CreateInvitationAsync(whiteboardId, invitee.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, invitee.Id, invitee.DisplayName, "conn-temp");

        await hub.Hub.JoinBoard(whiteboardId);

        Assert.Contains(("conn-temp", WhiteboardHub.GetGroupName(whiteboardId)), hub.RecordingGroups.Added);
        Assert.Contains(hub.GroupProxy.SentMessages, message => message.Method == "UserJoined" && (string)message.Args[0] == invitee.Id && (string)message.Args[1] == invitee.DisplayName);
    }

    [Fact]
    public async Task JoinBoard_SucceedsForProjectMember_OnSavedBoard()
    {
        var owner = await CreateUserAsync("owner-project@test.com", "Owner");
        var member = await CreateUserAsync("member-project@test.com", "Member");
        var projectId = await CreateProjectWithMembersAsync(owner.Id, member.Id);
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Saved Board", projectId);

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, member.Id, member.DisplayName, "conn-project");

        await hub.Hub.JoinBoard(whiteboardId);

        Assert.Contains(("conn-project", WhiteboardHub.GetGroupName(whiteboardId)), hub.RecordingGroups.Added);
        Assert.Contains(hub.GroupProxy.SentMessages, message => message.Method == "UserJoined" && (string)message.Args[0] == member.Id);
    }

    [Fact]
    public async Task JoinBoard_SucceedsForSiteAdmin()
    {
        var owner = await CreateUserAsync("owner-admin@test.com", "Owner");
        var admin = await CreateUserAsync("admin@test.com", "Admin", true);
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Admin Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, admin.Id, admin.DisplayName, "conn-admin", isAdmin: true);

        await hub.Hub.JoinBoard(whiteboardId);

        Assert.Contains(("conn-admin", WhiteboardHub.GetGroupName(whiteboardId)), hub.RecordingGroups.Added);
    }

    [Fact]
    public async Task JoinBoard_FailsForUnauthorizedUser()
    {
        var owner = await CreateUserAsync("owner-unauth@test.com", "Owner");
        var outsider = await CreateUserAsync("outsider@test.com", "Outsider");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Protected Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, outsider.Id, outsider.DisplayName, "conn-outsider");

        await Assert.ThrowsAsync<HubException>(() => hub.Hub.JoinBoard(whiteboardId));
    }

    [Fact]
    public async Task LeaveBoard_SucceedsAndBroadcastsUserLeft()
    {
        var owner = await CreateUserAsync("owner-leave@test.com", "Owner");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Leave Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-leave");
        await hub.Hub.JoinBoard(whiteboardId);

        await hub.Hub.LeaveBoard(whiteboardId);

        Assert.Contains(("conn-leave", WhiteboardHub.GetGroupName(whiteboardId)), hub.RecordingGroups.Removed);
        Assert.Contains(hub.GroupProxy.SentMessages, message => message.Method == "UserLeft" && (string)message.Args[0] == owner.Id);
    }

    [Fact]
    public async Task SendCanvasUpdate_AsPresenter_SavesAndBroadcastsCanvas()
    {
        var owner = await CreateUserAsync("canvas-owner@test.com", "Owner");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Canvas Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-canvas");
        var canvasData = "{\"shapes\":[{\"id\":\"shape-1\"}]}";
        await hub.Hub.JoinBoard(whiteboardId);

        await hub.Hub.SendCanvasUpdate(whiteboardId, canvasData);

        using var verificationScope = _factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whiteboard = context.Whiteboards.Single(w => w.Id == whiteboardId);

        Assert.Equal(canvasData, whiteboard.CanvasData);
        Assert.Contains(hub.GroupProxy.SentMessages, message => message.Method == "CanvasUpdated" && (string)message.Args[0] == canvasData);
    }

    [Fact]
    public async Task SendCanvasUpdate_AsNonPresenter_ThrowsHubException()
    {
        var owner = await CreateUserAsync("canvas-protected-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("canvas-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Protected Canvas Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, viewer.Id, viewer.DisplayName, "conn-viewer");
        await hub.Hub.JoinBoard(whiteboardId);

        await Assert.ThrowsAsync<HubException>(() => hub.Hub.SendCanvasUpdate(whiteboardId, "{}"));
    }

    [Fact]
    public async Task SendCanvasUpdate_WhenNotConnected_ThrowsHubException()
    {
        var owner = await CreateUserAsync("canvas-disconnected-owner@test.com", "Owner");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Disconnected Canvas Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-canvas-disconnected");

        await Assert.ThrowsAsync<HubException>(() => hub.Hub.SendCanvasUpdate(whiteboardId, "{}"));
    }

    [Fact]
    public async Task OnDisconnectedAsync_WhenPresenterDisconnects_ClearsPresenterAndBroadcastsChange()
    {
        var owner = await CreateUserAsync("disconnect-owner@test.com", "Owner");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Disconnect Presenter Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-disconnect-owner");
        await hub.Hub.JoinBoard(whiteboardId);

        await hub.Hub.OnDisconnectedAsync(null);

        using var verificationScope = _factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whiteboard = context.Whiteboards.Single(w => w.Id == whiteboardId);

        Assert.Null(whiteboard.CurrentPresenterId);
        Assert.Contains(hub.GroupProxy.SentMessages, message => message.Method == "PresenterChanged" && message.Args[0] == null);
        Assert.Contains(hub.GroupProxy.SentMessages, message => message.Method == "UserLeft" && (string)message.Args[0] == owner.Id);
    }

    [Fact]
    public async Task SendChatMessage_SavesAndBroadcastsMessage()
    {
        var owner = await CreateUserAsync("chat-owner@test.com", "Owner");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Chat Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-chat");
        await hub.Hub.JoinBoard(whiteboardId);

        await hub.Hub.SendChatMessage(whiteboardId, "Hello whiteboard chat");

        using var verificationScope = _factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedMessage = context.WhiteboardChatMessages.Single(m => m.WhiteboardId == whiteboardId);

        Assert.Equal("Hello whiteboard chat", savedMessage.Content);
        Assert.Contains(hub.GroupProxy.SentMessages, message => message.Method == "ChatMessageReceived");
    }

    [Fact]
    public async Task SendChatMessage_MessageExceeding4000Characters_IsRejected()
    {
        var owner = await CreateUserAsync("chat-limit-owner@test.com", "Owner");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Chat Limit Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-chat-limit");
        await hub.Hub.JoinBoard(whiteboardId);

        await Assert.ThrowsAsync<HubException>(() => hub.Hub.SendChatMessage(whiteboardId, new string('a', 4001)));
    }

    [Fact]
    public async Task SendChatMessage_UnauthorizedUserCannotSendChatMessages()
    {
        var owner = await CreateUserAsync("chat-protected-owner@test.com", "Owner");
        var outsider = await CreateUserAsync("chat-outsider@test.com", "Outsider");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Protected Chat Board");

        using var scope = _factory.Services.CreateScope();
        var hub = CreateHub(scope, outsider.Id, outsider.DisplayName, "conn-chat-outsider");

        await Assert.ThrowsAsync<HubException>(() => hub.Hub.SendChatMessage(whiteboardId, "Should fail"));
    }

    [Fact]
    public async Task AssignPresenter_OwnerCanAssignPresenterToActiveViewer()
    {
        var owner = await CreateUserAsync("presenter-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("presenter-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Presenter Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var ownerHub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-owner");
        var viewerHub = CreateHub(scope, viewer.Id, viewer.DisplayName, "conn-viewer");
        await viewerHub.Hub.JoinBoard(whiteboardId);

        await ownerHub.Hub.AssignPresenter(whiteboardId, viewer.Id);

        using var verificationScope = _factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whiteboard = context.Whiteboards.Single(w => w.Id == whiteboardId);

        Assert.Equal(viewer.Id, whiteboard.CurrentPresenterId);
        Assert.Contains(ownerHub.GroupProxy.SentMessages, message => message.Method == "PresenterChanged" && (string)message.Args[0] == viewer.Id);
    }

    [Fact]
    public async Task AssignPresenter_OwnerCannotAssignPresenterToNonActiveUser()
    {
        var owner = await CreateUserAsync("presenter-owner-inactive@test.com", "Owner");
        var viewer = await CreateUserAsync("presenter-viewer-inactive@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Presenter Inactive Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var ownerHub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-owner-inactive");

        await Assert.ThrowsAsync<HubException>(() => ownerHub.Hub.AssignPresenter(whiteboardId, viewer.Id));
    }

    [Fact]
    public async Task AssignPresenter_NonOwnerCannotAssignPresenter()
    {
        var owner = await CreateUserAsync("presenter-protected-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("presenter-protected-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Protected Presenter Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var viewerHub = CreateHub(scope, viewer.Id, viewer.DisplayName, "conn-viewer-protected");
        await viewerHub.Hub.JoinBoard(whiteboardId);

        await Assert.ThrowsAsync<HubException>(() => viewerHub.Hub.AssignPresenter(whiteboardId, viewer.Id));
    }

    [Fact]
    public async Task ReclaimPresenter_OwnerCanReclaimPresenter()
    {
        var owner = await CreateUserAsync("reclaim-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("reclaim-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Reclaim Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var setupScope = _factory.Services.CreateScope();
        var viewerHub = CreateHub(setupScope, viewer.Id, viewer.DisplayName, "conn-reclaim-viewer");
        await viewerHub.Hub.JoinBoard(whiteboardId);

        using var scope = _factory.Services.CreateScope();
        var ownerHub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-reclaim-owner");
        await ownerHub.Hub.AssignPresenter(whiteboardId, viewer.Id);
        await ownerHub.Hub.ReclaimPresenter(whiteboardId);

        using var verificationScope = _factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whiteboard = context.Whiteboards.Single(w => w.Id == whiteboardId);

        Assert.Equal(owner.Id, whiteboard.CurrentPresenterId);
        Assert.Contains(ownerHub.GroupProxy.SentMessages, message => message.Method == "PresenterChanged" && (string)message.Args[0] == owner.Id);
    }

    [Fact]
    public async Task ReclaimPresenter_NonOwnerCannotReclaimPresenter()
    {
        var owner = await CreateUserAsync("reclaim-protected-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("reclaim-protected-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Protected Reclaim Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var viewerHub = CreateHub(scope, viewer.Id, viewer.DisplayName, "conn-reclaim-protected-viewer");

        await Assert.ThrowsAsync<HubException>(() => viewerHub.Hub.ReclaimPresenter(whiteboardId));
    }

    [Fact]
    public async Task RemoveUser_OwnerCanRemoveUserFromTemporaryBoard()
    {
        var owner = await CreateUserAsync("remove-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("remove-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Remove User Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var ownerHub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-remove-owner");
        var viewerHub = CreateHub(scope, viewer.Id, viewer.DisplayName, "conn-remove-viewer");
        await viewerHub.Hub.JoinBoard(whiteboardId);

        await ownerHub.Hub.RemoveUser(whiteboardId, viewer.Id);

        using var verificationScope = _factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(context.WhiteboardInvitations.SingleOrDefault(i => i.WhiteboardId == whiteboardId && i.UserId == viewer.Id));
    }

    [Fact]
    public async Task RemoveUser_OwnerCannotRemoveUserFromSavedBoard()
    {
        var owner = await CreateUserAsync("remove-saved-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("remove-saved-viewer@test.com", "Viewer");
        var projectId = await CreateProjectWithMembersAsync(owner.Id, viewer.Id);
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Saved Remove Board", projectId);

        using var scope = _factory.Services.CreateScope();
        var ownerHub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-remove-saved-owner");

        await Assert.ThrowsAsync<HubException>(() => ownerHub.Hub.RemoveUser(whiteboardId, viewer.Id));
    }

    [Fact]
    public async Task RemoveUser_NonOwnerCannotRemoveUser()
    {
        var owner = await CreateUserAsync("remove-protected-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("remove-protected-viewer@test.com", "Viewer");
        var other = await CreateUserAsync("remove-protected-other@test.com", "Other");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Protected Remove Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);
        await CreateInvitationAsync(whiteboardId, other.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var otherHub = CreateHub(scope, other.Id, other.DisplayName, "conn-remove-protected-other");

        await Assert.ThrowsAsync<HubException>(() => otherHub.Hub.RemoveUser(whiteboardId, viewer.Id));
    }

    [Fact]
    public async Task RemoveUser_RemovedUserReceivesUserRemovedEvent()
    {
        var owner = await CreateUserAsync("remove-event-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("remove-event-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Remove Event Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var ownerHub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-remove-event-owner");
        var viewerHub = CreateHub(scope, viewer.Id, viewer.DisplayName, "conn-remove-event-viewer");
        await viewerHub.Hub.JoinBoard(whiteboardId);

        await ownerHub.Hub.RemoveUser(whiteboardId, viewer.Id);

        Assert.Contains(ownerHub.GroupProxy.SentMessages, message => message.Method == "UserLeft" && (string)message.Args[0] == viewer.Id);
    }

    [Fact]
    public async Task RemoveUser_IfRemovedUserWasPresenter_PresenterIsCleared()
    {
        var owner = await CreateUserAsync("remove-presenter-owner@test.com", "Owner");
        var viewer = await CreateUserAsync("remove-presenter-viewer@test.com", "Viewer");
        var whiteboardId = await CreateWhiteboardAsync(owner.Id, "Remove Presenter Board");
        await CreateInvitationAsync(whiteboardId, viewer.Id, owner.Id);

        using var scope = _factory.Services.CreateScope();
        var ownerHub = CreateHub(scope, owner.Id, owner.DisplayName, "conn-remove-presenter-owner");
        var viewerHub = CreateHub(scope, viewer.Id, viewer.DisplayName, "conn-remove-presenter-viewer");
        await viewerHub.Hub.JoinBoard(whiteboardId);
        await ownerHub.Hub.AssignPresenter(whiteboardId, viewer.Id);

        await ownerHub.Hub.RemoveUser(whiteboardId, viewer.Id);

        using var verificationScope = _factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whiteboard = context.Whiteboards.Single(w => w.Id == whiteboardId);

        Assert.Null(whiteboard.CurrentPresenterId);
        Assert.Contains(ownerHub.GroupProxy.SentMessages, message => message.Method == "PresenterChanged" && message.Args[0] == null);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private HubTestHarness CreateHub(IServiceScope scope, string userId, string displayName, string connectionId, bool isAdmin = false)
    {
        var hub = new WhiteboardHub(
            scope.ServiceProvider.GetRequiredService<IWhiteboardService>(),
            scope.ServiceProvider.GetRequiredService<IWhiteboardInvitationService>(),
            scope.ServiceProvider.GetRequiredService<IWhiteboardChatService>(),
            scope.ServiceProvider.GetRequiredService<IWhiteboardPresenceTracker>(),
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            NullLogger<WhiteboardHub>.Instance);

        var groupProxy = new RecordingClientProxy();
        var clients = new RecordingHubCallerClients(groupProxy);
        var groups = new RecordingGroupManager();

        hub.Context = new TestHubCallerContext(connectionId, userId, displayName, isAdmin);
        hub.Clients = clients;
        hub.Groups = groups;

        return new HubTestHarness(hub, groups, groupProxy);
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email = "whiteboardhub@test.com", string displayName = "Whiteboard Hub User")
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

    private async Task<ApplicationUser> CreateUserAsync(string email, string displayName, bool isAdmin = false)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            if (isAdmin && !await userManager.IsInRoleAsync(existing, SeedData.AdminRoleName))
            {
                await userManager.AddToRoleAsync(existing, SeedData.AdminRoleName);
            }

            return existing;
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

        return user;
    }

    private async Task<int> CreateProjectWithMembersAsync(string ownerUserId, string memberUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project
        {
            Name = $"Whiteboard Project {Guid.NewGuid():N}",
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

    private async Task<int> CreateWhiteboardAsync(string ownerUserId, string title, int? projectId = null)
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

    private async Task CreateInvitationAsync(int whiteboardId, string userId, string invitedByUserId)
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

    private sealed record HubTestHarness(WhiteboardHub Hub, RecordingGroupManager RecordingGroups, RecordingClientProxy GroupProxy);

    private sealed class TestHubCallerContext : HubCallerContext
    {
        private readonly ClaimsPrincipal _user;

        public TestHubCallerContext(string connectionId, string userId, string displayName, bool isAdmin)
        {
            ConnectionId = connectionId;
            UserIdentifier = userId;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(ClaimTypes.Name, displayName)
            };

            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, SeedData.AdminRoleName));
            }

            _user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        public override string ConnectionId { get; }
        public override string UserIdentifier { get; }
        public override ClaimsPrincipal User => _user;
        private readonly Dictionary<object, object?> _items = new();

        public override IDictionary<object, object?> Items => _items;
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = new();
        public List<(string ConnectionId, string GroupName)> Removed { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHubCallerClients : IHubCallerClients
    {
        private readonly RecordingClientProxy _groupProxy;
        private readonly RecordingClientProxy _singleProxy = new();

        public RecordingHubCallerClients(RecordingClientProxy groupProxy)
        {
            _groupProxy = groupProxy;
        }

        public IClientProxy All => _singleProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _singleProxy;
        public IClientProxy Caller => _singleProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _singleProxy;
        public IClientProxy Group(string groupName) => _groupProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _groupProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _groupProxy;
        public IClientProxy Others => _singleProxy;
        public IClientProxy OthersInGroup(string groupName) => _groupProxy;
        public IClientProxy Client(string connectionId) => _singleProxy;
        public IClientProxy User(string userId) => _singleProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _singleProxy;
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> SentMessages { get; } = new();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            SentMessages.Add((method, args));
            return Task.CompletedTask;
        }
    }
}
