using TeamWare.Agent.Configuration;

namespace TeamWare.Agent.Tests.Configuration;

public class McpServerOptionsTests
{
    [Fact]
    public void DefaultValues_AreApplied()
    {
        var options = new McpServerOptions();

        Assert.Equal(string.Empty, options.Name);
        Assert.Equal(string.Empty, options.Type);
        Assert.Null(options.Url);
        Assert.Null(options.AuthHeader);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new McpServerOptions
        {
            Name = "teamware",
            Type = "http",
            Url = "https://localhost:5001/mcp",
            AuthHeader = "Bearer token123"
        };

        Assert.Equal("teamware", options.Name);
        Assert.Equal("http", options.Type);
        Assert.Equal("https://localhost:5001/mcp", options.Url);
        Assert.Equal("Bearer token123", options.AuthHeader);
    }
}
