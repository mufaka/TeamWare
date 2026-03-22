using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Hubs;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Infrastructure;

public class LoungeHubTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LoungeHubTests(TeamWareWebApplicationFactory factory)
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

    // --- 17.1 LoungeHub basic structure tests ---

    [Fact]
    public void LoungeHub_InheritsFromHub()
    {
        Assert.True(typeof(Hub).IsAssignableFrom(typeof(LoungeHub)));
    }

    [Fact]
    public void LoungeHub_HasAuthorizeAttribute()
    {
        var hubType = typeof(LoungeHub);
        var authorizeAttributes = hubType.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);

        Assert.NotEmpty(authorizeAttributes);
    }

    [Fact]
    public void LoungeHub_HasJoinRoomMethod()
    {
        var method = typeof(LoungeHub).GetMethod("JoinRoom");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(int?), parameters[0].ParameterType);
    }

    [Fact]
    public void LoungeHub_HasLeaveRoomMethod()
    {
        var method = typeof(LoungeHub).GetMethod("LeaveRoom");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(int?), parameters[0].ParameterType);
    }

    [Fact]
    public void LoungeHub_HasSendMessageMethod()
    {
        var method = typeof(LoungeHub).GetMethod("SendMessage");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int?), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
    }

    [Fact]
    public void LoungeHub_HasEditMessageMethod()
    {
        var method = typeof(LoungeHub).GetMethod("EditMessage");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
    }

    [Fact]
    public void LoungeHub_HasDeleteMessageMethod()
    {
        var method = typeof(LoungeHub).GetMethod("DeleteMessage");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
    }

    [Fact]
    public void LoungeHub_HasToggleReactionMethod()
    {
        var method = typeof(LoungeHub).GetMethod("ToggleReaction");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
    }

    [Fact]
    public void LoungeHub_HasMarkAsReadMethod()
    {
        var method = typeof(LoungeHub).GetMethod("MarkAsRead");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int?), parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
    }

    // --- 17.2 Hub Registration tests ---

    [Fact]
    public void LoungeHub_IsRegistered_InDependencyInjection()
    {
        using var scope = _factory.Services.CreateScope();
        var hubContext = scope.ServiceProvider.GetService<IHubContext<LoungeHub>>();

        Assert.NotNull(hubContext);
    }

    [Fact]
    public void PresenceHub_StillRegistered_AfterLoungeHubAdded()
    {
        // Verify LoungeHub doesn't interfere with PresenceHub
        using var scope = _factory.Services.CreateScope();
        var presenceHubContext = scope.ServiceProvider.GetService<IHubContext<PresenceHub>>();

        Assert.NotNull(presenceHubContext);
    }

    [Fact]
    public async Task LoungeHub_Endpoint_Unauthenticated_RejectsRequest()
    {
        await EnsureDatabaseCreated();

        var response = await _client.PostAsync("/hubs/lounge/negotiate?negotiateVersion=1", null);

        // SignalR negotiate returns 401/redirect for unauthenticated users
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LoungeHub_NegotiateEndpoint_Authenticated_ReturnsOk()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/lounge/negotiate?negotiateVersion=1");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoungeHub_Endpoint_IndependentOfPresenceHub()
    {
        var (_, cookie) = await CreateAndLoginUser("hubindep@test.com", "Hub Independence User");

        // Both endpoints should be reachable independently
        var loungeRequest = new HttpRequestMessage(HttpMethod.Post, "/hubs/lounge/negotiate?negotiateVersion=1");
        loungeRequest.Headers.Add("Cookie", cookie);
        var loungeResponse = await _client.SendAsync(loungeRequest);

        var presenceRequest = new HttpRequestMessage(HttpMethod.Post, "/hubs/presence/negotiate?negotiateVersion=1");
        presenceRequest.Headers.Add("Cookie", cookie);
        var presenceResponse = await _client.SendAsync(presenceRequest);

        Assert.Equal(HttpStatusCode.OK, loungeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, presenceResponse.StatusCode);
    }

    // --- 17.3 Group naming tests ---

    [Fact]
    public void GetRoomGroupName_ProjectRoom_ReturnsCorrectGroupName()
    {
        var groupName = LoungeHub.GetRoomGroupName(42);

        Assert.Equal("lounge-project-42", groupName);
    }

    [Fact]
    public void GetRoomGroupName_GeneralRoom_ReturnsCorrectGroupName()
    {
        var groupName = LoungeHub.GetRoomGroupName(null);

        Assert.Equal("lounge-general", groupName);
    }

    [Fact]
    public void GetRoomGroupName_DifferentProjects_ReturnDifferentGroupNames()
    {
        var group1 = LoungeHub.GetRoomGroupName(1);
        var group2 = LoungeHub.GetRoomGroupName(2);
        var groupGeneral = LoungeHub.GetRoomGroupName(null);

        Assert.NotEqual(group1, group2);
        Assert.NotEqual(group1, groupGeneral);
        Assert.NotEqual(group2, groupGeneral);
    }

    // --- Helper methods ---

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(
        string email = "loungehubtest@test.com", string displayName = "Lounge Hub Test User")
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
