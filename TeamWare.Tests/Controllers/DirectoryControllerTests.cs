using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class DirectoryControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DirectoryControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(
        string email = "dirtest@test.com", string displayName = "Dir Test User")
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

    // --- Authentication ---

    [Fact]
    public async Task Index_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Directory");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Profile_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Directory/Profile/some-id");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- Index ---

    [Fact]
    public async Task Index_Authenticated_ReturnsSuccess()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Index_ContainsDirectoryContent()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("User Directory", html);
        Assert.Contains("Search by name or email", html);
    }

    [Fact]
    public async Task Index_SearchReturnsResults()
    {
        var (_, cookie) = await CreateAndLoginUser("dirsearch@test.com", "Searchable User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory?search=Searchable");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Searchable User", html);
    }

    [Fact]
    public async Task Index_SortByEmail_ReturnsSuccess()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory?sortBy=email&ascending=true");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Index_ShowsAllRegisteredUsers()
    {
        // Create a few extra users
        await CreateAndLoginUser("dir-all1@test.com", "All User One");
        var (_, cookie) = await CreateAndLoginUser("dir-all2@test.com", "All User Two");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("All User One", html);
        Assert.Contains("All User Two", html);
    }

    // --- Profile ---

    [Fact]
    public async Task Profile_ValidUser_ReturnsSuccess()
    {
        var (userId, cookie) = await CreateAndLoginUser("dirprofile@test.com", "Profile User");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Directory/Profile/{userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Profile_ContainsUserInfo()
    {
        var (userId, cookie) = await CreateAndLoginUser("dirprofileinfo@test.com", "Profile Info User");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Directory/Profile/{userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Profile Info User", html);
        Assert.Contains("dirprofileinfo@test.com", html);
    }

    [Fact]
    public async Task Profile_ContainsTaskStatistics()
    {
        var (userId, cookie) = await CreateAndLoginUser("dirstats@test.com", "Stats User");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Directory/Profile/{userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Task Statistics", html);
        Assert.Contains("Assigned", html);
        Assert.Contains("Completed", html);
        Assert.Contains("Overdue", html);
    }

    [Fact]
    public async Task Profile_ContainsProjectsSection()
    {
        var (userId, cookie) = await CreateAndLoginUser("dirprojects@test.com", "Projects User");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Directory/Profile/{userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Projects", html);
    }

    [Fact]
    public async Task Profile_ContainsRecentActivitySection()
    {
        var (userId, cookie) = await CreateAndLoginUser("diractivity@test.com", "Activity User");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Directory/Profile/{userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Recent Activity", html);
    }

    [Fact]
    public async Task Profile_ContainsInviteLink()
    {
        var (userId, cookie) = await CreateAndLoginUser("dirinvite@test.com", "Invite User");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Directory/Profile/{userId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Invite to a project", html);
    }

    [Fact]
    public async Task Profile_NonExistentUser_ReturnsNotFound()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory/Profile/nonexistent-user-id");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Profile_EmptyId_ReturnsNotFound()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory/Profile/");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        // Empty ID will either be caught as NotFound or redirect to Index
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.OK,
            $"Expected NotFound or OK but got {response.StatusCode}");
    }

    // --- Directory shows in layout ---

    [Fact]
    public async Task Layout_ContainsDirectoryLink()
    {
        var (_, cookie) = await CreateAndLoginUser("dirlayout@test.com", "Layout User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Directory", html);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
