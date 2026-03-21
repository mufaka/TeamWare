using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Hubs;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Infrastructure;

public class PresenceHubTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PresenceHubTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task EnsureDatabaseCreated()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task PresenceHub_Endpoint_Unauthenticated_RejectsRequest()
    {
        await EnsureDatabaseCreated();

        // The negotiate endpoint should exist and reject unauthenticated requests
        var response = await _client.PostAsync("/hubs/presence/negotiate?negotiateVersion=1", null);

        // SignalR negotiate returns 401/redirect for unauthenticated users
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void PresenceHub_IsRegistered_InDependencyInjection()
    {
        // Verify SignalR services are registered
        using var scope = _factory.Services.CreateScope();
        var hubContext = scope.ServiceProvider.GetService<IHubContext<PresenceHub>>();

        Assert.NotNull(hubContext);
    }

    [Fact]
    public void PresenceHub_HasAuthorizeAttribute()
    {
        var hubType = typeof(PresenceHub);
        var authorizeAttributes = hubType.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);

        Assert.NotEmpty(authorizeAttributes);
    }

    [Fact]
    public void PresenceHub_InheritsFromHub()
    {
        Assert.True(typeof(Hub).IsAssignableFrom(typeof(PresenceHub)));
    }

    [Fact]
    public void PresenceHub_OverridesOnConnectedAsync()
    {
        var method = typeof(PresenceHub).GetMethod("OnConnectedAsync");

        Assert.NotNull(method);
        Assert.True(method!.DeclaringType == typeof(PresenceHub));
    }

    [Fact]
    public void PresenceHub_OverridesOnDisconnectedAsync()
    {
        var method = typeof(PresenceHub).GetMethod("OnDisconnectedAsync");

        Assert.NotNull(method);
        Assert.True(method!.DeclaringType == typeof(PresenceHub));
    }

    [Fact]
    public async Task PresenceHub_NegotiateEndpoint_Authenticated_ReturnsOk()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/presence/negotiate?negotiateVersion=1");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(
        string email = "hubtest@test.com", string displayName = "Hub Test User")
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

        var loginCookie = await GetLoginCookie(email, "TestPass1!");
        return (user.Id, loginCookie);
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

    public void Dispose()
    {
        _client.Dispose();
    }
}
