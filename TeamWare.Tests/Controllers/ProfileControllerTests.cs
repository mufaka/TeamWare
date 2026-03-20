using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class ProfileControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProfileControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "profile-test@test.com")
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
                DisplayName = "Profile Test User"
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

    // --- Index ---

    [Fact]
    public async Task Index_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Profile");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Index_Authenticated_ReturnsSuccess()
    {
        var loginCookie = await CreateAndLoginUser("profile-index@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Profile");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("My Profile", html);
        Assert.Contains("profile-index@test.com", html);
    }

    [Fact]
    public async Task Index_ShowsThemePreference()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Profile");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Theme Preference", html);
    }

    // --- Edit ---

    [Fact]
    public async Task Edit_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Profile/Edit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Edit_Get_ReturnsForm()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Profile/Edit");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Edit Profile", html);
        Assert.Contains("Profile Test User", html);
    }

    [Fact]
    public async Task Edit_Post_UpdatesProfileAndRedirects()
    {
        var loginCookie = await CreateAndLoginUser("profile-edit@test.com");

        // Get edit page for anti-forgery token
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/Profile/Edit");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var responseCookies = getResponse.Headers.GetValues("Set-Cookie");
        var allCookies = loginCookie + "; " + string.Join("; ", responseCookies);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Profile/Edit");
        request.Headers.Add("Cookie", allCookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["DisplayName"] = "Updated Name",
            ["AvatarUrl"] = "https://example.com/avatar.jpg",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Profile", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Edit_Post_EmptyDisplayName_ReturnsValidationError()
    {
        var loginCookie = await CreateAndLoginUser();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/Profile/Edit");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var responseCookies = getResponse.Headers.GetValues("Set-Cookie");
        var allCookies = loginCookie + "; " + string.Join("; ", responseCookies);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Profile/Edit");
        request.Headers.Add("Cookie", allCookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["DisplayName"] = "",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("required", html, StringComparison.OrdinalIgnoreCase);
    }

    // --- ChangePassword ---

    [Fact]
    public async Task ChangePassword_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Profile/ChangePassword");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ChangePassword_Get_ReturnsForm()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Profile/ChangePassword");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Change Password", html);
    }

    [Fact]
    public async Task ChangePassword_Post_WrongCurrentPassword_ShowsError()
    {
        var loginCookie = await CreateAndLoginUser("password-test@test.com");

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/Profile/ChangePassword");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var responseCookies = getResponse.Headers.GetValues("Set-Cookie");
        var allCookies = loginCookie + "; " + string.Join("; ", responseCookies);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Profile/ChangePassword");
        request.Headers.Add("Cookie", allCookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CurrentPassword"] = "WrongPass1!",
            ["NewPassword"] = "NewPass123!",
            ["ConfirmPassword"] = "NewPass123!",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Layout ---

    [Fact]
    public async Task Layout_ShowsProfileLink_WhenAuthenticated()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("My Profile", html);
    }

    // --- Dashboard ---

    [Fact]
    public async Task Dashboard_Authenticated_ShowsDashboard()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Dashboard", html);
    }

    [Fact]
    public async Task Dashboard_Unauthenticated_ShowsWelcomePage()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Welcome to TeamWare", html);
    }

    [Fact]
    public async Task Dashboard_HasHtmxLazyLoadSections()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("DashboardInbox", html);
        Assert.Contains("DashboardWhatsNext", html);
        Assert.Contains("DashboardProjects", html);
        Assert.Contains("DashboardDeadlines", html);
        Assert.Contains("DashboardReview", html);
        Assert.Contains("DashboardNotifications", html);
    }

    [Fact]
    public async Task DashboardInbox_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Home/DashboardInbox");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DashboardInbox_Authenticated_ReturnsPartial()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardInbox");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Inbox", html);
    }

    [Fact]
    public async Task DashboardWhatsNext_Authenticated_ReturnsPartial()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardWhatsNext");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DashboardProjects_Authenticated_ReturnsPartial()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardProjects");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DashboardDeadlines_Authenticated_ReturnsPartial()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardDeadlines");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DashboardNotifications_Authenticated_ReturnsPartial()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardNotifications");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
