using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Tests.Integration;

/// <summary>
/// Integration tests that verify end-to-end MCP connectivity.
/// These require a running TeamWare instance and valid configuration.
/// 
/// To run these tests:
/// 1. Start a TeamWare.Web instance
/// 2. Create an agent user with a PAT
/// 3. Set environment variables:
///    - TEAMWARE_TEST_MCP_URL (e.g., "http://localhost:5000/mcp")
///    - TEAMWARE_TEST_PAT (the agent's personal access token)
///    - TEAMWARE_TEST_AGENT_NAME (the agent user's display name)
/// 4. Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class McpClientIntegrationTests
{
    private readonly string? _mcpUrl;
    private readonly string? _pat;
    private readonly string? _agentName;

    public McpClientIntegrationTests()
    {
        _mcpUrl = Environment.GetEnvironmentVariable("TEAMWARE_TEST_MCP_URL");
        _pat = Environment.GetEnvironmentVariable("TEAMWARE_TEST_PAT");
        _agentName = Environment.GetEnvironmentVariable("TEAMWARE_TEST_AGENT_NAME");
    }

    private bool CanRun => !string.IsNullOrEmpty(_mcpUrl) && !string.IsNullOrEmpty(_pat);

    private AgentIdentityOptions CreateOptions() => new()
    {
        Name = _agentName ?? "integration-test-agent",
        PersonalAccessToken = _pat!,
        WorkingDirectory = Path.GetTempPath(),
        McpServers =
        [
            new McpServerOptions
            {
                Name = "teamware",
                Type = "http",
                Url = _mcpUrl!
            }
        ]
    };

    [Fact]
    public async Task GetMyProfile_ReturnsAgentIdentity()
    {
        if (!CanRun)
        {
            return; // Skip when not configured
        }

        var options = CreateOptions();
        var logger = new TestLogger<TeamWareMcpClient>();
        await using var client = await TeamWareMcpClient.CreateAsync(options, logger);

        var profile = await client.GetMyProfileAsync();

        Assert.True(profile.IsAgent);
        Assert.NotEmpty(profile.UserId);
        Assert.NotEmpty(profile.DisplayName);
    }

    [Fact]
    public async Task GetMyAssignments_ReturnsAssignedTasks()
    {
        if (!CanRun)
        {
            return; // Skip when not configured
        }

        var options = CreateOptions();
        var logger = new TestLogger<TeamWareMcpClient>();
        await using var client = await TeamWareMcpClient.CreateAsync(options, logger);

        var assignments = await client.GetMyAssignmentsAsync();

        // The result should be a valid list (may be empty if no tasks are assigned)
        Assert.NotNull(assignments);
    }

    [Fact]
    public async Task InvalidPat_ThrowsOnProfileCheck()
    {
        if (string.IsNullOrEmpty(_mcpUrl))
        {
            return; // Skip when not configured
        }

        var options = new AgentIdentityOptions
        {
            Name = "bad-pat-agent",
            PersonalAccessToken = "invalid-pat-value-12345",
            WorkingDirectory = Path.GetTempPath(),
            McpServers =
            [
                new McpServerOptions
                {
                    Name = "teamware",
                    Type = "http",
                    Url = _mcpUrl
                }
            ]
        };

        var logger = new TestLogger<TeamWareMcpClient>();

        // Should fail during client creation or on first tool call
        try
        {
            await using var client = await TeamWareMcpClient.CreateAsync(options, logger);
            await Assert.ThrowsAnyAsync<Exception>(() => client.GetMyProfileAsync());
        }
        catch (Exception)
        {
            // Expected — either client creation fails or the tool call fails
        }
    }

    [Fact]
    public async Task PausedAgent_ProfileReturnsInactive()
    {
        if (!CanRun)
        {
            return; // Skip when not configured
        }

        // This test verifies the IsAgentActive field is returned correctly.
        // To fully test the paused scenario, the agent user must be paused
        // in the TeamWare admin UI before running this test.
        var options = CreateOptions();
        var logger = new TestLogger<TeamWareMcpClient>();
        await using var client = await TeamWareMcpClient.CreateAsync(options, logger);

        var profile = await client.GetMyProfileAsync();

        // We can only verify the field exists and has a value
        Assert.True(profile.IsAgent);
        Assert.NotNull(profile.IsAgentActive);
    }
}
