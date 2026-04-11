using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Views;

public class LayoutSmokeTests : IClassFixture<TeamWareWebApplicationFactory>
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LayoutSmokeTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [Fact]
    public async Task Layout_ContainsTailwindCssLink()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("site.min.css", html);
    }

    [Fact]
    public async Task Layout_ContainsAlpineJsScript()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("alpine.min.js", html);
    }

    [Fact]
    public async Task Layout_ContainsHtmxScript()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("htmx.min.js", html);
    }

    [Fact]
    public async Task Layout_ContainsDarkModeToggle()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-testid=\"theme-toggle\"", html);
    }

    [Fact]
    public async Task Layout_DoesNotReferenceBootstrapOrJQuery()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("bootstrap", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Layout_ContainsSidebarNavigation()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("sidebarOpen", html);
        Assert.Contains("<aside", html);
    }

    [Fact]
    public async Task LoginPage_UsesAspnetValidationScript()
    {
        var response = await _client.GetAsync("/Account/Login");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("aspnet-validation.min.js", html);
        Assert.Contains("aspnetValidation.ValidationService", html);
    }

    [Fact]
    public async Task Layout_UnauthenticatedUser_ShowsLoginAndRegisterLinks()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Log in", html);
        Assert.Contains("Register", html);
        Assert.DoesNotContain("Log out", html);
    }

    [Fact]
    public async Task Layout_AuthenticatedUser_ShowsWhiteboardsLink()
    {
        var cookie = await CreateAndLoginUser("layout-whiteboards@test.com", "Layout User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Whiteboards", html);
        Assert.Contains("/Whiteboard", html);
    }

    [Fact]
    public async Task Layout_UnauthenticatedUser_DoesNotShowWhiteboardsLink()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("/Whiteboard", html);
    }

    private async Task<string> CreateAndLoginUser(string email, string displayName)
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
}
