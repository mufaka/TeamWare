using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class NotificationControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public NotificationControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "notif-test@test.com")
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
                DisplayName = "Notification Test User"
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

    private async Task<int> CreateNotification(string userEmail = "notif-test@test.com")
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(userEmail);

        var notification = new Notification
        {
            UserId = user!.Id,
            Message = "Test notification",
            Type = NotificationType.TaskAssigned,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            ReferenceId = 1
        };
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();
        return notification.Id;
    }

    private async Task<(string UserId, int NotificationId, int WhiteboardId)> CreateWhiteboardInvitationNotification(string userEmail, bool createInvitation = true, bool savedBoard = false, bool removeMembership = false)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var invitee = await userManager.FindByEmailAsync(userEmail);
        var owner = new ApplicationUser
        {
            UserName = $"owner-{Guid.NewGuid():N}@test.com",
            Email = $"owner-{Guid.NewGuid():N}@test.com",
            DisplayName = "Whiteboard Owner"
        };
        await userManager.CreateAsync(owner, "TestPass1!");

        int? projectId = null;
        if (savedBoard)
        {
            var project = new Project
            {
                Name = $"Whiteboard Project {Guid.NewGuid():N}",
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

            if (!removeMembership)
            {
                context.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = project.Id,
                    UserId = invitee!.Id,
                    Role = ProjectRole.Member,
                    JoinedAt = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync();
            projectId = project.Id;
        }

        var whiteboard = new Whiteboard
        {
            Title = "Notification Whiteboard",
            OwnerId = owner.Id,
            CurrentPresenterId = owner.Id,
            ProjectId = projectId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Whiteboards.Add(whiteboard);
        await context.SaveChangesAsync();

        if (createInvitation)
        {
            context.WhiteboardInvitations.Add(new WhiteboardInvitation
            {
                WhiteboardId = whiteboard.Id,
                UserId = invitee!.Id,
                InvitedByUserId = owner.Id,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var notification = new Notification
        {
            UserId = invitee!.Id,
            Message = "Whiteboard invite",
            Type = NotificationType.WhiteboardInvitation,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            ReferenceId = whiteboard.Id
        };
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        return (invitee.Id, notification.Id, whiteboard.Id);
    }

    // --- Index ---

    [Fact]
    public async Task Index_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Notification");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Index_Authenticated_ReturnsSuccess()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Notification");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Notifications", html);
    }

    [Fact]
    public async Task Index_ShowsNotifications()
    {
        var loginCookie = await CreateAndLoginUser();
        await CreateNotification();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Notification");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Test notification", html);
        Assert.Contains("Mark read", html);
        Assert.Contains("Dismiss", html);
    }

    // --- MarkAsRead ---

    [Fact]
    public async Task MarkAsRead_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Notification/MarkAsRead?id=1");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task MarkAsRead_Authenticated_RedirectsToIndex()
    {
        var loginCookie = await CreateAndLoginUser();
        var notifId = await CreateNotification();

        // Get page for anti-forgery token
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/Notification");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var responseCookies = getResponse.Headers.GetValues("Set-Cookie");
        var allCookies = loginCookie + "; " + string.Join("; ", responseCookies);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Notification/MarkAsRead?id={notifId}");
        request.Headers.Add("Cookie", allCookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Notification", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Follow_WhiteboardInvitation_RedirectsToWhiteboardSession()
    {
        var loginCookie = await CreateAndLoginUser("whiteboard-follow@test.com");
        var (_, notificationId, whiteboardId) = await CreateWhiteboardInvitationNotification("whiteboard-follow@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Notification/Follow/{notificationId}");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Whiteboard/Session/{whiteboardId}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Follow_DeletedWhiteboardInvitation_CleansUpInvitationAndRedirectsToLandingPage()
    {
        var loginCookie = await CreateAndLoginUser("whiteboard-follow-deleted@test.com");
        var (userId, notificationId, whiteboardId) = await CreateWhiteboardInvitationNotification("whiteboard-follow-deleted@test.com");

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var whiteboard = await context.Whiteboards.FindAsync(whiteboardId);
            context.Whiteboards.Remove(whiteboard!);
            await context.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Notification/Follow/{notificationId}");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Whiteboard", response.Headers.Location?.ToString());

        using var verificationScope = _factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await verificationContext.WhiteboardInvitations.SingleOrDefaultAsync(i => i.WhiteboardId == whiteboardId && i.UserId == userId));
    }

    [Fact]
    public async Task Follow_SavedWhiteboardWithoutMembership_CleansUpInvitationAndRedirectsToLandingPage()
    {
        var loginCookie = await CreateAndLoginUser("whiteboard-follow-removed@test.com");
        var (userId, notificationId, whiteboardId) = await CreateWhiteboardInvitationNotification("whiteboard-follow-removed@test.com", savedBoard: true, removeMembership: true);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Notification/Follow/{notificationId}");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Whiteboard", response.Headers.Location?.ToString());

        using var verificationScope = _factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await verificationContext.WhiteboardInvitations.SingleOrDefaultAsync(i => i.WhiteboardId == whiteboardId && i.UserId == userId));
    }

    // --- Dismiss ---

    [Fact]
    public async Task Dismiss_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Notification/Dismiss?id=1");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Dismiss_Authenticated_RedirectsToIndex()
    {
        var loginCookie = await CreateAndLoginUser();
        var notifId = await CreateNotification();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/Notification");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var responseCookies = getResponse.Headers.GetValues("Set-Cookie");
        var allCookies = loginCookie + "; " + string.Join("; ", responseCookies);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Notification/Dismiss?id={notifId}");
        request.Headers.Add("Cookie", allCookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Notification", response.Headers.Location?.ToString());
    }

    // --- Layout integration ---

    [Fact]
    public async Task Layout_ShowsNotificationNavLink()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Notifications", html);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
