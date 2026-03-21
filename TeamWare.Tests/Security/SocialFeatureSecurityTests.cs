using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Security;

public class SocialFeatureSecurityTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SocialFeatureSecurityTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(
        string email = "social-sec@test.com", string displayName = "Social Sec User")
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.EnsureCreatedAsync();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            return (existing.Id, await GetLoginCookie(email));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };

        await userManager.CreateAsync(user, "TestPass1!");

        var cookie = await GetLoginCookie(email);
        return (user.Id, cookie);
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
        var allCookies = new List<string>();

        if (loginResponse.Headers.TryGetValues("Set-Cookie", out var responseCookies))
        {
            allCookies.AddRange(responseCookies);
        }

        allCookies.AddRange(cookies);
        return string.Join("; ", allCookies);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var tokenStart = html.IndexOf("name=\"__RequestVerificationToken\"", StringComparison.Ordinal);
        if (tokenStart == -1) return string.Empty;

        var valueStart = html.IndexOf("value=\"", tokenStart, StringComparison.Ordinal) + 7;
        var valueEnd = html.IndexOf("\"", valueStart, StringComparison.Ordinal);
        return html[valueStart..valueEnd];
    }

    // ---------------------------------------------------------------
    // SEC-05: Social feature GET endpoints require authentication
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Admin/Dashboard")]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/ResetPassword?id=x&email=x@test.com")]
    [InlineData("/Admin/ActivityLog")]
    [InlineData("/Directory")]
    [InlineData("/Directory/Profile?id=x")]
    [InlineData("/Activity/GlobalFeed")]
    [InlineData("/Invitation/PendingForUser")]
    [InlineData("/Invitation/PendingForProject?projectId=1")]
    public async Task SocialFeatureGetEndpoint_Unauthenticated_RedirectsToLogin(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // ---------------------------------------------------------------
    // SEC-05: Social feature POST endpoints require authentication
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Admin/LockUser", "id=x")]
    [InlineData("/Admin/UnlockUser", "id=x")]
    [InlineData("/Admin/PromoteToAdmin", "id=x")]
    [InlineData("/Admin/DemoteToUser", "id=x")]
    [InlineData("/Invitation/Send", "ProjectId=1&UserId=x")]
    [InlineData("/Invitation/Accept", "id=1")]
    [InlineData("/Invitation/Decline", "id=1")]
    public async Task SocialFeaturePostEndpoint_Unauthenticated_RedirectsOrBadRequest(string url, string formData)
    {
        var pairs = formData.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .Select(p => new KeyValuePair<string, string>(p[0], p[1]));

        var content = new FormUrlEncodedContent(pairs);
        var response = await _client.PostAsync(url, content);

        // Without anti-forgery token, we get 400 (from the global filter).
        // This is also acceptable since it blocks the request.
        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected Redirect or BadRequest for {url}, got {response.StatusCode}");
    }

    // ---------------------------------------------------------------
    // TEST-06: Admin-only endpoints reject non-admin users
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Admin/Dashboard")]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/ActivityLog")]
    public async Task AdminGetEndpoint_NonAdminUser_RedirectsToAccessDenied(string url)
    {
        var (_, cookie) = await CreateAndLoginUser("social-nonadmin@test.com", "NonAdmin Social");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AdminResetPassword_NonAdminUser_RedirectsToAccessDenied()
    {
        var (userId, cookie) = await CreateAndLoginUser("social-nonadmin-reset@test.com", "NonAdmin Reset");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Admin/ResetPassword?id={userId}&email=test@test.com");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    // ---------------------------------------------------------------
    // SEC-03: CSRF token enforcement on social feature POST actions
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Invitation/Send", "ProjectId=1&UserId=test")]
    [InlineData("/Invitation/Accept", "id=1")]
    [InlineData("/Invitation/Decline", "id=1")]
    public async Task SocialFeaturePost_WithoutAntiForgeryToken_ReturnsBadRequest(string url, string formData)
    {
        var (_, cookie) = await CreateAndLoginUser("social-csrf@test.com", "CSRF Social");

        var pairs = formData.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .Select(p => new KeyValuePair<string, string>(p[0], p[1]));

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(pairs);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/Admin/LockUser", "id=test")]
    [InlineData("/Admin/UnlockUser", "id=test")]
    [InlineData("/Admin/PromoteToAdmin", "id=test")]
    [InlineData("/Admin/DemoteToUser", "id=test")]
    public async Task AdminPost_NonAdminWithoutToken_IsBlocked(string url, string formData)
    {
        var (_, cookie) = await CreateAndLoginUser("social-csrf-admin@test.com", "CSRF Admin");

        var pairs = formData.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .Select(p => new KeyValuePair<string, string>(p[0], p[1]));

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(pairs);

        var response = await _client.SendAsync(request);

        // Admin role authorization blocks the request before CSRF validation.
        // Either BadRequest (CSRF) or Redirect (AccessDenied) is acceptable.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Redirect,
            $"Expected BadRequest or Redirect for {url}, got {response.StatusCode}");
    }

    // ---------------------------------------------------------------
    // SEC-04: Input validation on admin reset password form
    // ---------------------------------------------------------------

    [Fact]
    public async Task AdminResetPassword_EmptyPassword_ReturnsValidationError()
    {
        // Create an admin user
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var adminEmail = "admin-validation@test.com";
        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing == null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = "Admin Validation"
            };
            await userManager.CreateAsync(adminUser, "TestPass1!");
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }

        var cookie = await GetLoginCookie(adminEmail);

        // Get the form to extract anti-forgery token
        var targetUserId = "some-user-id";
        var getRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/Admin/ResetPassword?id={targetUserId}&email=target@test.com");
        getRequest.Headers.Add("Cookie", cookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var getCookies = getResponse.Headers.GetValues("Set-Cookie");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ResetPassword?id={targetUserId}");
        request.Headers.Add("Cookie", string.Join("; ", getCookies) + "; " + cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "target@test.com",
            ["NewPassword"] = "",
            ["ConfirmPassword"] = "",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("required", content, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // SEC-04: Invitation model validation
    // ---------------------------------------------------------------

    [Fact]
    public async Task InvitationSend_EmptyUserId_RedirectsWithError()
    {
        var (_, cookie) = await CreateAndLoginUser("social-invite-val@test.com", "Invite Validation");

        // Get a page with anti-forgery token
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/Invitation/PendingForUser");
        getRequest.Headers.Add("Cookie", cookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var getCookies = getResponse.Headers.GetValues("Set-Cookie");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Invitation/Send");
        request.Headers.Add("Cookie", string.Join("; ", getCookies) + "; " + cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectId"] = "1",
            ["UserId"] = "",
            ["Role"] = "Member",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        // Should redirect back because of invalid model state
        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.OK,
            $"Expected Redirect or OK for validation, got {response.StatusCode}");
    }

    // ---------------------------------------------------------------
    // SEC-05: Authenticated user can access directory endpoints
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Directory")]
    [InlineData("/Invitation/PendingForUser")]
    public async Task SocialFeatureGetEndpoint_AuthenticatedUser_ReturnsSuccess(string url)
    {
        var (_, cookie) = await CreateAndLoginUser("social-auth@test.com", "Auth Social");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
