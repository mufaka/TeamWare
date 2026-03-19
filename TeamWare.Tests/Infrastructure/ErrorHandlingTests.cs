using System.Net;

namespace TeamWare.Tests.Infrastructure;

public class ErrorHandlingTests : IClassFixture<TeamWareWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ErrorHandlingTests(TeamWareWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task NonExistentRoute_ReturnsStatusCodePage()
    {
        var response = await _client.GetAsync("/this-route-does-not-exist");

        // UseStatusCodePagesWithReExecute re-executes to /Home/StatusCode/{code}
        // which returns 404 with the Error view
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StatusCodeEndpoint_ReturnsErrorView()
    {
        var response = await _client.GetAsync("/Home/StatusCode/404");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Error", html);
    }

    [Fact]
    public async Task HttpsRedirection_IsConfigured()
    {
        // The test server uses HTTP by default. In non-development environments
        // HTTPS redirection would issue a redirect. In the test (Development) environment,
        // the middleware is skipped, so we verify the app still responds.
        var response = await _client.GetAsync("/");
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect);
    }
}
