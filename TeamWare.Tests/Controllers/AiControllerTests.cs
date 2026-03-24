using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class AiControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "ai-test@test.com")
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
                DisplayName = "AI Test User"
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

    private async Task<(string Cookie, string Token)> GetAuthenticatedRequestParts(string email = "ai-test@test.com")
    {
        var loginCookie = await CreateAndLoginUser(email);

        // Get a page with an anti-forgery token
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        getRequest.Headers.Add("Cookie", loginCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var content = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(content);

        // Combine login cookie with any new cookies from the GET
        var allCookies = loginCookie;
        if (getResponse.Headers.Contains("Set-Cookie"))
        {
            allCookies += "; " + string.Join("; ", getResponse.Headers.GetValues("Set-Cookie"));
        }

        return (allCookies, token);
    }

    // --- Authentication Required ---

    [Fact]
    public async Task IsAvailable_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Ai/IsAvailable");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task RewriteProjectDescription_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/RewriteProjectDescription");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task RewriteTaskDescription_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/RewriteTaskDescription");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PolishComment_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/PolishComment");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ExpandInboxItem_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/ExpandInboxItem");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ProjectSummary_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/ProjectSummary");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PersonalDigest_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/PersonalDigest");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ReviewPreparation_Unauthenticated_RedirectsToLogin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ai/ReviewPreparation");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- IsAvailable returns false when OLLAMA_URL is empty ---

    [Fact]
    public async Task IsAvailable_ReturnsAvailableFalse_WhenOllamaUrlIsEmpty()
    {
        var loginCookie = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Get, "/Ai/IsAvailable");
        request.Headers.Add("Cookie", loginCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"available\":false", content);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
