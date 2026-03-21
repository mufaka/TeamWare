using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class AdminControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> LoginAsAdmin()
    {
        return await GetLoginCookie(SeedData.AdminEmail, SeedData.AdminPassword);
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email = "regular@test.com", string displayName = "Regular User")
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

    private async Task<(string Token, string Cookies)> GetFormTokenAndCookies(string url, string authCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", authCookie);
        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(content);

        var allCookies = new List<string> { authCookie };
        if (response.Headers.TryGetValues("Set-Cookie", out var responseCookies))
        {
            allCookies.AddRange(responseCookies);
        }

        return (token, string.Join("; ", allCookies));
    }

    // --- Authorization: Unauthenticated ---

    [Fact]
    public async Task Dashboard_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Admin/Dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Users_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ActivityLog_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Admin/ActivityLog");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- Authorization: Non-admin user ---

    [Fact]
    public async Task Dashboard_NonAdmin_RedirectsToAccessDenied()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Dashboard");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Users_NonAdmin_RedirectsToAccessDenied()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Users");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ActivityLog_NonAdmin_RedirectsToAccessDenied()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/ActivityLog");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    // --- Admin access ---

    [Fact]
    public async Task Dashboard_Admin_ReturnsSuccess()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Dashboard");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin Dashboard", content);
    }

    [Fact]
    public async Task Users_Admin_ReturnsSuccess()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Users");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("User Management", content);
    }

    [Fact]
    public async Task ActivityLog_Admin_ReturnsSuccess()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/ActivityLog");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin Activity Log", content);
    }

    [Fact]
    public async Task Dashboard_Admin_ShowsStatistics()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Dashboard");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Total Users", content);
        Assert.Contains("Total Projects", content);
        Assert.Contains("Total Tasks", content);
    }

    [Fact]
    public async Task Users_Admin_SearchWorks()
    {
        var cookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin/Users?search=admin");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(SeedData.AdminEmail, content);
    }

    [Fact]
    public async Task LockUser_Admin_RedirectsToUsers()
    {
        var (targetId, _) = await CreateAndLoginUser("locktest@test.com", "Lock Test User");
        var adminCookie = await LoginAsAdmin();

        var (token, cookies) = await GetFormTokenAndCookies("/Admin/Users", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/LockUser?id={targetId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Users", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task UnlockUser_Admin_RedirectsToUsers()
    {
        var (targetId, _) = await CreateAndLoginUser("unlocktest@test.com", "Unlock Test User");
        var adminCookie = await LoginAsAdmin();

        var (token, cookies) = await GetFormTokenAndCookies("/Admin/Users", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/UnlockUser?id={targetId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Users", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PromoteToAdmin_Admin_RedirectsToUsers()
    {
        var (targetId, _) = await CreateAndLoginUser("promotetest@test.com", "Promote Test User");
        var adminCookie = await LoginAsAdmin();

        var (token, cookies) = await GetFormTokenAndCookies("/Admin/Users", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/PromoteToAdmin?id={targetId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Users", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DemoteToUser_Admin_RedirectsToUsers()
    {
        var (targetId, _) = await CreateAndLoginUser("demotetest@test.com", "Demote Test User");
        var adminCookie = await LoginAsAdmin();

        // First promote the user
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var target = await userManager.FindByIdAsync(targetId);
            if (target != null)
            {
                await userManager.AddToRoleAsync(target, SeedData.AdminRoleName);
            }
        }

        var (token, cookies) = await GetFormTokenAndCookies("/Admin/Users", adminCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/DemoteToUser?id={targetId}");
        request.Headers.Add("Cookie", cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/Users", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ResetPassword_Get_Admin_ReturnsSuccess()
    {
        var (targetId, _) = await CreateAndLoginUser("resetpwtest@test.com", "Reset PW Test");
        var adminCookie = await LoginAsAdmin();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/ResetPassword?id={targetId}&email=resetpwtest@test.com");
        request.Headers.Add("Cookie", adminCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Reset Password", content);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
