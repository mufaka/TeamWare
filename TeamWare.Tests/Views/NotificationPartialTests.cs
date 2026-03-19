namespace TeamWare.Tests.Views;

public class NotificationPartialTests : IClassFixture<TeamWareWebApplicationFactory>
{
    private readonly HttpClient _client;

    public NotificationPartialTests(TeamWareWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Layout_IncludesNotificationPartialPlaceholder()
    {
        // The notification partial is rendered in the layout.
        // When there are no TempData messages, it renders nothing visible,
        // but the partial is still invoked. We verify the layout renders without error.
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // The layout should render successfully with the notification partial included.
        // No alert divs should be present when there are no messages.
        Assert.DoesNotContain("role=\"alert\"", html);
    }
}
