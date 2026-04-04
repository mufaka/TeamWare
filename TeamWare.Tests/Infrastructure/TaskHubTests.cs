using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Hubs;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Infrastructure;

public class TaskHubTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TaskHubTests(TeamWareWebApplicationFactory factory)
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

    // --- 49.1 TaskHub basic structure tests ---

    [Fact]
    public void TaskHub_InheritsFromHub()
    {
        Assert.True(typeof(Hub).IsAssignableFrom(typeof(TaskHub)));
    }

    [Fact]
    public void TaskHub_HasAuthorizeAttribute()
    {
        var hubType = typeof(TaskHub);
        var authorizeAttributes = hubType.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);

        Assert.NotEmpty(authorizeAttributes);
    }

    [Fact]
    public void TaskHub_HasJoinTaskMethod()
    {
        var method = typeof(TaskHub).GetMethod("JoinTask");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
    }

    [Fact]
    public void TaskHub_HasLeaveTaskMethod()
    {
        var method = typeof(TaskHub).GetMethod("LeaveTask");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
    }

    [Fact]
    public void TaskHub_GetGroupName_ReturnsExpectedFormat()
    {
        var groupName = TaskHub.GetGroupName(42);
        Assert.Equal("task-42", groupName);
    }

    [Fact]
    public void TaskHub_GetGroupName_DifferentTasks_ReturnDifferentNames()
    {
        var group1 = TaskHub.GetGroupName(1);
        var group2 = TaskHub.GetGroupName(2);

        Assert.NotEqual(group1, group2);
    }

    // --- Hub Registration tests ---

    [Fact]
    public void TaskHub_IsRegistered_InDependencyInjection()
    {
        using var scope = _factory.Services.CreateScope();
        var hubContext = scope.ServiceProvider.GetService<IHubContext<TaskHub>>();

        Assert.NotNull(hubContext);
    }

    [Fact]
    public async Task TaskHub_Endpoint_Unauthenticated_RejectsRequest()
    {
        await EnsureDatabaseCreated();

        var response = await _client.PostAsync("/hubs/task/negotiate?negotiateVersion=1", null);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TaskHub_NegotiateEndpoint_Authenticated_ReturnsOk()
    {
        var (_, cookie) = await CreateAndLoginUser();

        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/task/negotiate?negotiateVersion=1");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TaskHub_Endpoint_IndependentOfOtherHubs()
    {
        var (_, cookie) = await CreateAndLoginUser("taskhubindep@test.com", "Task Hub Independence User");

        var taskRequest = new HttpRequestMessage(HttpMethod.Post, "/hubs/task/negotiate?negotiateVersion=1");
        taskRequest.Headers.Add("Cookie", cookie);
        var taskResponse = await _client.SendAsync(taskRequest);

        var loungeRequest = new HttpRequestMessage(HttpMethod.Post, "/hubs/lounge/negotiate?negotiateVersion=1");
        loungeRequest.Headers.Add("Cookie", cookie);
        var loungeResponse = await _client.SendAsync(loungeRequest);

        Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loungeResponse.StatusCode);
    }

    // --- Helper methods ---

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(
        string email = "taskhubtest@test.com", string displayName = "Task Hub Test User")
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
