using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class ReviewControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ReviewControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "review-test@test.com")
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
                DisplayName = "Review Test User"
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
        var response = await _client.GetAsync("/Review");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Index_Authenticated_ReturnsSuccess()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Review");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Weekly Review", html);
    }

    [Fact]
    public async Task Index_DefaultsToStep1()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Review");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Step 1: Unprocessed Inbox Items", html);
    }

    [Fact]
    public async Task Index_Step2_ShowsActiveTasks()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Review?step=2");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Step 2: Active Tasks", html);
    }

    [Fact]
    public async Task Index_Step3_ShowsSomedayMaybe()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Review?step=3");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Step 3: Someday/Maybe", html);
        Assert.Contains("Complete Your Review", html);
    }

    [Fact]
    public async Task Index_InvalidStep_DefaultsToStep1()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Review?step=99");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Step 1: Unprocessed Inbox Items", html);
    }

    // --- Complete ---

    [Fact]
    public async Task Complete_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Review/Complete");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Complete_Authenticated_CreatesReviewAndRedirects()
    {
        var loginCookie = await CreateAndLoginUser();

        // Get page for anti-forgery token
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/Review?step=3");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getContent);
        var responseCookies = getResponse.Headers.GetValues("Set-Cookie");
        var allCookies = loginCookie + "; " + string.Join("; ", responseCookies);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Review/Complete");
        request.Headers.Add("Cookie", allCookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Notes"] = "Weekly review complete",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Review", response.Headers.Location?.ToString());
    }

    // --- Layout integration ---

    [Fact]
    public async Task Layout_ShowsWeeklyReviewNavLink()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Weekly Review", html);
    }

    [Fact]
    public async Task Home_ShowsReviewDashboardSection()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Dashboard loads review status via HTMX lazy-load
        Assert.Contains("DashboardReview", html);
        Assert.Contains("Weekly Review", html);
    }

    [Fact]
    public async Task DashboardReview_ShowsReviewDueIndicator()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardReview");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // No reviews completed yet so review is due
        Assert.Contains("Review due", html);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
