using System.Net;

namespace TeamWare.Tests.Views;

public class LayoutSmokeTests : IClassFixture<TeamWareWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LayoutSmokeTests(TeamWareWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
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
}
