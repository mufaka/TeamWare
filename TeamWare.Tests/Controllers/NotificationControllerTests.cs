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
