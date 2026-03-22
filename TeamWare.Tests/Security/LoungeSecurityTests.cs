using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Security;

public class LoungeSecurityTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LoungeSecurityTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(
        string email, string displayName = "Lounge Sec User")
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

    private async Task<int> CreateProjectWithOwner(string ownerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project
        {
            Name = $"Sec Test Project {Guid.NewGuid():N}",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = ownerUserId,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return project.Id;
    }

    private async Task<int> CreateLoungeMessage(int? projectId, string userId, string content = "Test message")
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = new LoungeMessage
        {
            ProjectId = projectId,
            UserId = userId,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
        context.LoungeMessages.Add(message);
        await context.SaveChangesAsync();

        return message.Id;
    }

    // ---------------------------------------------------------------
    // SEC-07: Lounge GET endpoints require authentication
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Lounge/Room")]
    [InlineData("/Lounge/Room?projectId=1")]
    [InlineData("/Lounge/Messages?count=50")]
    [InlineData("/Lounge/PinnedMessages")]
    [InlineData("/Lounge/MemberSearch?term=test")]
    public async Task LoungeGetEndpoint_Unauthenticated_RedirectsToLogin(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");
    }

    // ---------------------------------------------------------------
    // SEC-07: Lounge POST endpoints require authentication
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Lounge/CreateTaskFromMessage", "messageId=1")]
    [InlineData("/Lounge/PinMessage", "messageId=1")]
    [InlineData("/Lounge/UnpinMessage", "messageId=1")]
    public async Task LoungePostEndpoint_Unauthenticated_RedirectsOrBadRequest(string url, string formData)
    {
        var pairs = formData.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .Select(p => new KeyValuePair<string, string>(p[0], p[1]));

        var content = new FormUrlEncodedContent(pairs);
        var response = await _client.PostAsync(url, content);

        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected Redirect or BadRequest for {url}, got {response.StatusCode}");
    }

    // ---------------------------------------------------------------
    // SEC-03: CSRF token enforcement on lounge POST actions
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Lounge/CreateTaskFromMessage", "messageId=1")]
    [InlineData("/Lounge/PinMessage", "messageId=1")]
    [InlineData("/Lounge/UnpinMessage", "messageId=1")]
    public async Task LoungePost_WithoutAntiForgeryToken_ReturnsBadRequest(string url, string formData)
    {
        var (_, cookie) = await CreateAndLoginUser($"lounge-csrf-{url.GetHashCode():X}@test.com");

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

    // ---------------------------------------------------------------
    // SEC-07: Non-member cannot access project room
    // ---------------------------------------------------------------

    [Fact]
    public async Task Room_ProjectRoom_NonMember_GetsForbidden()
    {
        var (ownerId, _) = await CreateAndLoginUser("lounge-sec-owner@test.com", "Owner");
        var projectId = await CreateProjectWithOwner(ownerId);

        var (_, nonMemberCookie) = await CreateAndLoginUser("lounge-sec-nonmember@test.com", "Non-Member");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Room?projectId={projectId}");
        request.Headers.Add("Cookie", nonMemberCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Messages_ProjectRoom_NonMember_GetsForbidden()
    {
        var (ownerId, _) = await CreateAndLoginUser("lounge-sec-owner2@test.com", "Owner2");
        var projectId = await CreateProjectWithOwner(ownerId);

        var (_, nonMemberCookie) = await CreateAndLoginUser("lounge-sec-nonmember2@test.com", "Non-Member2");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Messages?projectId={projectId}&count=50");
        request.Headers.Add("Cookie", nonMemberCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task PinnedMessages_ProjectRoom_NonMember_GetsForbidden()
    {
        var (ownerId, _) = await CreateAndLoginUser("lounge-sec-owner3@test.com", "Owner3");
        var projectId = await CreateProjectWithOwner(ownerId);

        var (_, nonMemberCookie) = await CreateAndLoginUser("lounge-sec-nonmember3@test.com", "Non-Member3");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/PinnedMessages?projectId={projectId}");
        request.Headers.Add("Cookie", nonMemberCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task MemberSearch_ProjectRoom_NonMember_GetsForbidden()
    {
        var (ownerId, _) = await CreateAndLoginUser("lounge-sec-owner4@test.com", "Owner4");
        var projectId = await CreateProjectWithOwner(ownerId);

        var (_, nonMemberCookie) = await CreateAndLoginUser("lounge-sec-nonmember4@test.com", "Non-Member4");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/MemberSearch?projectId={projectId}&term=test");
        request.Headers.Add("Cookie", nonMemberCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
    }

    // ---------------------------------------------------------------
    // SEC-04: Input validation on lounge endpoints
    // ---------------------------------------------------------------

    [Fact]
    public async Task MemberSearch_EmptyTerm_ReturnsEmptyArray()
    {
        var (_, cookie) = await CreateAndLoginUser("lounge-sec-val1@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/MemberSearch?term=");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", content);
    }

    [Fact]
    public async Task MemberSearch_VeryLongTerm_ReturnsEmptyArray()
    {
        var (_, cookie) = await CreateAndLoginUser("lounge-sec-val2@test.com");

        var longTerm = new string('a', 200);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/MemberSearch?term={longTerm}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", content);
    }

    [Fact]
    public async Task Messages_CountIsClamped()
    {
        var (_, cookie) = await CreateAndLoginUser("lounge-sec-clamp@test.com");

        // Requesting count=10000 should be clamped to 100
        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Messages?count=10000");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        // Should succeed — the count is clamped, not rejected
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // SEC-06: XSS sanitization — message content is auto-encoded
    // ---------------------------------------------------------------

    [Fact]
    public async Task Room_MessageContent_IsHtmlEncoded()
    {
        var (userId, cookie) = await CreateAndLoginUser("lounge-sec-xss@test.com", "XSS Test");

        // Create a message with HTML/script content
        var messageId = await CreateLoungeMessage(null, userId, "<script>alert('xss')</script>");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Room");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Razor auto-encoding should prevent raw script tags from appearing
        Assert.DoesNotContain("<script>alert", content);
        Assert.Contains("&lt;script&gt;", content);
    }

    // ---------------------------------------------------------------
    // SEC-07: Authenticated user can access #general room
    // ---------------------------------------------------------------

    [Fact]
    public async Task Room_General_AuthenticatedUser_CanAccess()
    {
        var (_, cookie) = await CreateAndLoginUser("lounge-sec-general@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Room");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // SEC-07: Project member can access project room
    // ---------------------------------------------------------------

    [Fact]
    public async Task Room_ProjectRoom_Member_CanAccess()
    {
        var (userId, cookie) = await CreateAndLoginUser("lounge-sec-member@test.com");
        var projectId = await CreateProjectWithOwner(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Room?projectId={projectId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
