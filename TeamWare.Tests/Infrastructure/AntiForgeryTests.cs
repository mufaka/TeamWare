using System.Net;

namespace TeamWare.Tests.Infrastructure;

public class AntiForgeryTests : IClassFixture<TeamWareWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AntiForgeryTests(TeamWareWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task PostWithoutAntiForgeryToken_ReturnsBadRequest()
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", "test@example.com" },
            { "Password", "TestPassword1" }
        });

        var response = await _client.PostAsync("/Account/Login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
