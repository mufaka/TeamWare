using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Views;

public class UiConsistencyTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UiConsistencyTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> GetLoginCookie(string email)
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
                DisplayName = "UI Test User"
            };
            await userManager.CreateAsync(user, "TestPass1!");
        }

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

    // ---------------------------------------------------------------
    // UI-01: Responsive layout is present
    // ---------------------------------------------------------------

    [Fact]
    public async Task Layout_ContainsViewportMetaTag()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("viewport", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("width=device-width", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Layout_ContainsMobileMenuToggle()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("sidebarOpen", html);
        Assert.Contains("md:hidden", html);
    }

    // ---------------------------------------------------------------
    // UI-02: Dark mode toggle is present and functional
    // ---------------------------------------------------------------

    [Fact]
    public async Task Layout_ContainsDarkModeToggle()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("darkMode", html);
        Assert.Contains("theme-toggle", html);
    }

    [Fact]
    public async Task Layout_HtmlUsesAlpineJsDarkClass()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(":class=\"{ 'dark': darkMode }\"", html);
    }

    // ---------------------------------------------------------------
    // UI-02: Dark mode classes on all pages
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/Account/AccessDenied")]
    public async Task PublicPage_ContainsDarkModeClasses(string url)
    {
        var response = await _client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("dark:", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Project")]
    [InlineData("/Project/Create")]
    [InlineData("/Inbox")]
    [InlineData("/Inbox/Add")]
    [InlineData("/Notification")]
    [InlineData("/Profile")]
    [InlineData("/Profile/ChangePassword")]
    [InlineData("/Directory")]
    [InlineData("/Invitation/PendingForUser")]
    public async Task AuthenticatedPage_ContainsDarkModeClasses(string url)
    {
        var cookie = await GetLoginCookie("ui-dark@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("dark:", html, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------
    // UI-07: No emoji or emoticon characters in rendered HTML
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task PublicPage_NoEmojiCharacters(string url)
    {
        var response = await _client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        AssertNoEmojis(html, url);
    }

    [Theory]
    [InlineData("/Project")]
    [InlineData("/Inbox")]
    [InlineData("/Profile")]
    [InlineData("/Directory")]
    [InlineData("/Invitation/PendingForUser")]
    public async Task AuthenticatedPage_NoEmojiCharacters(string url)
    {
        var cookie = await GetLoginCookie("ui-emoji@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        AssertNoEmojis(html, url);
    }

    // ---------------------------------------------------------------
    // UI-03: Key pages render without errors
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/Project")]
    [InlineData("/Inbox")]
    [InlineData("/Inbox/Add")]
    [InlineData("/Home/WhatsNext")]
    [InlineData("/Notification")]
    [InlineData("/Review")]
    [InlineData("/Profile")]
    [InlineData("/Profile/Edit")]
    [InlineData("/Profile/ChangePassword")]
    [InlineData("/Directory")]
    [InlineData("/Invitation/PendingForUser")]
    public async Task AuthenticatedPage_ReturnsSuccess(string url)
    {
        var cookie = await GetLoginCookie("ui-render@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // UI-01/UI-02/UI-03: Admin page consistency (requires admin role)
    // ---------------------------------------------------------------

    private async Task<string> GetAdminLoginCookie()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var adminEmail = "ui-admin@test.com";
        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing == null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = "UI Admin User"
            };
            await userManager.CreateAsync(adminUser, "TestPass1!");
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }

        return await GetLoginCookie(adminEmail);
    }

    [Theory]
    [InlineData("/Admin/Dashboard")]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/ActivityLog")]
    public async Task AdminPage_ContainsDarkModeClasses(string url)
    {
        var cookie = await GetAdminLoginCookie();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("dark:", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Admin/Dashboard")]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/ActivityLog")]
    public async Task AdminPage_ReturnsSuccess(string url)
    {
        var cookie = await GetAdminLoginCookie();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/Admin/Dashboard")]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/ActivityLog")]
    public async Task AdminPage_NoEmojiCharacters(string url)
    {
        var cookie = await GetAdminLoginCookie();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        AssertNoEmojis(html, url);
    }

    // ---------------------------------------------------------------
    // UI-08: Sidebar navigation structure and grouping
    // ---------------------------------------------------------------

    [Fact]
    public async Task Sidebar_ContainsExpectedNavItems()
    {
        var cookie = await GetLoginCookie("ui-nav@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Nav items may render with whitespace between tags in Razor output
        Assert.Contains("Home", html, StringComparison.Ordinal);
        Assert.Contains(">Notifications</span>", html, StringComparison.Ordinal);
        Assert.Contains("Invitations", html, StringComparison.Ordinal);
        Assert.Contains("Projects", html, StringComparison.Ordinal);
        Assert.Contains(">Inbox</span>", html, StringComparison.Ordinal);
        Assert.Contains("What\u0027s Next", html, StringComparison.Ordinal);
        Assert.Contains("Someday/Maybe", html, StringComparison.Ordinal);
        Assert.Contains("Weekly Review", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sidebar_DoesNotContainPrivacyLink()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Privacy", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sidebar_ProjectSubItems_AreIndented()
    {
        var cookie = await GetLoginCookie("ui-nav-indent@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Sub-items under Projects use pl-7 for visual indentation
        var inboxIndex = html.IndexOf(">Inbox</span>", StringComparison.Ordinal);
        var whatsNextIndex = html.IndexOf("What&#x27;s Next", StringComparison.Ordinal);
        if (whatsNextIndex < 0) whatsNextIndex = html.IndexOf("What\u0027s Next", StringComparison.Ordinal);
        var somedayIndex = html.IndexOf("Someday/Maybe", StringComparison.Ordinal);
        var reviewIndex = html.IndexOf("Weekly Review", StringComparison.Ordinal);

        Assert.True(inboxIndex > 0, "Inbox nav item not found");
        Assert.True(whatsNextIndex > 0, "What's Next nav item not found");
        Assert.True(somedayIndex > 0, "Someday/Maybe nav item not found");
        Assert.True(reviewIndex > 0, "Weekly Review nav item not found");

        // Verify each sub-item's containing anchor uses pl-7
        var inboxAnchorStart = html.LastIndexOf("<a ", inboxIndex, StringComparison.Ordinal);
        var whatsNextAnchorStart = html.LastIndexOf("<a ", whatsNextIndex, StringComparison.Ordinal);
        var somedayAnchorStart = html.LastIndexOf("<a ", somedayIndex, StringComparison.Ordinal);
        var reviewAnchorStart = html.LastIndexOf("<a ", reviewIndex, StringComparison.Ordinal);

        Assert.Contains("pl-7", html[inboxAnchorStart..inboxIndex]);
        Assert.Contains("pl-7", html[whatsNextAnchorStart..whatsNextIndex]);
        Assert.Contains("pl-7", html[somedayAnchorStart..somedayIndex]);
        Assert.Contains("pl-7", html[reviewAnchorStart..reviewIndex]);
    }

    [Fact]
    public async Task Sidebar_NavOrder_HomeNotificationsProjectsThenSubItems()
    {
        var cookie = await GetLoginCookie("ui-nav-order@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Find nav items in the sidebar - some items render inline, some multi-line
        var navStart = html.IndexOf("<nav", StringComparison.Ordinal);
        var homeIndex = html.IndexOf("Home", navStart, StringComparison.Ordinal);
        var notifIndex = html.IndexOf(">Notifications</span>", StringComparison.Ordinal);
        var invitationsIndex = html.IndexOf("Invitations", notifIndex, StringComparison.Ordinal);
        var projectsIndex = html.IndexOf("Projects", invitationsIndex, StringComparison.Ordinal);
        var inboxIndex = html.IndexOf(">Inbox</span>", StringComparison.Ordinal);
        var whatsNextIndex = html.IndexOf("What&#x27;s Next", StringComparison.Ordinal);
        if (whatsNextIndex < 0) whatsNextIndex = html.IndexOf("What\u0027s Next", StringComparison.Ordinal);
        var somedayIndex = html.IndexOf("Someday/Maybe", StringComparison.Ordinal);
        var reviewIndex = html.IndexOf("Weekly Review", StringComparison.Ordinal);

        Assert.True(homeIndex < notifIndex, "Home should appear before Notifications");
        Assert.True(notifIndex < invitationsIndex, "Notifications should appear before Invitations");
        Assert.True(invitationsIndex < projectsIndex, "Invitations should appear before Projects");
        Assert.True(projectsIndex < inboxIndex, "Projects should appear before Inbox");
        Assert.True(inboxIndex < whatsNextIndex, "Inbox should appear before What's Next");
        Assert.True(whatsNextIndex < somedayIndex, "What's Next should appear before Someday/Maybe");
        Assert.True(somedayIndex < reviewIndex, "Someday/Maybe should appear before Weekly Review");
    }

    private static void AssertNoEmojis(string html, string url)
    {
        // Check for common emoji Unicode ranges
        foreach (var c in html)
        {
            var codePoint = (int)c;

            // Emoticons (1F600-1F64F), Misc Symbols (1F300-1F5FF),
            // Transport/Map (1F680-1F6FF), Supplemental (1F900-1F9FF)
            // These are in the supplementary plane so they appear as surrogate pairs in C#.
            // We check for Dingbats (2700-27BF) and Misc Symbols (2600-26FF) which are in BMP.
            if (codePoint >= 0x2600 && codePoint <= 0x26FF)
            {
                Assert.Fail($"Emoji character found in {url}: U+{codePoint:X4}");
            }
            if (codePoint >= 0x2700 && codePoint <= 0x27BF)
            {
                Assert.Fail($"Dingbat/emoji character found in {url}: U+{codePoint:X4}");
            }
        }

        // Also check for high surrogates that might indicate supplementary emoji planes
        var emojiPattern = @"[\uD83C-\uD83F][\uDC00-\uDFFF]";
        Assert.False(Regex.IsMatch(html, emojiPattern), $"Supplementary emoji character found in {url}");
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
