using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class TaskControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TaskControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<string> CreateAndLoginUser(string email = "task-test@test.com")
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
                DisplayName = "Task Test User"
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

    [Fact]
    public async Task TaskIndex_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task?projectId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TaskCreate_Get_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task/Create?projectId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WhatsNext_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Home/WhatsNext");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WhatsNext_Authenticated_ReturnsSuccess()
    {
        var cookie = await CreateAndLoginUser("whatsnext-test@test.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/WhatsNext");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("What", content);
    }

    [Fact]
    public async Task TaskDetails_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Task/Details/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
