using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Repository;

namespace TeamWare.Agent.Tests.Repository;

public class RepositoryManagerTests
{
    private static AgentIdentityOptions CreateOptions(
        string name = "test-agent",
        string workingDirectory = "/tmp/test",
        string? repositoryUrl = null,
        string? repositoryBranch = null,
        string? repositoryAccessToken = null)
    {
        return new AgentIdentityOptions
        {
            Name = name,
            WorkingDirectory = workingDirectory,
            PersonalAccessToken = "test-pat",
            RepositoryUrl = repositoryUrl,
            RepositoryBranch = repositoryBranch,
            RepositoryAccessToken = repositoryAccessToken
        };
    }

    [Fact]
    public async Task EnsureRepositoryAsync_NoRepositoryUrl_DoesNothing()
    {
        var logger = new TestLogger<RepositoryManager>();
        var manager = new RepositoryManager(logger);
        var options = CreateOptions(repositoryUrl: null);

        await manager.EnsureRepositoryAsync(options, CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("No RepositoryUrl configured"));
    }

    [Fact]
    public async Task EnsureRepositoryAsync_EmptyRepositoryUrl_DoesNothing()
    {
        var logger = new TestLogger<RepositoryManager>();
        var manager = new RepositoryManager(logger);
        var options = CreateOptions(repositoryUrl: "");

        await manager.EnsureRepositoryAsync(options, CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("No RepositoryUrl configured"));
    }

    [Fact]
    public async Task EnsureRepositoryAsync_WhitespaceRepositoryUrl_DoesNothing()
    {
        var logger = new TestLogger<RepositoryManager>();
        var manager = new RepositoryManager(logger);
        var options = CreateOptions(repositoryUrl: "   ");

        await manager.EnsureRepositoryAsync(options, CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("No RepositoryUrl configured"));
    }

    [Theory]
    [InlineData("https://github.com/user/repo.git", null, "https://github.com/user/repo.git")]
    [InlineData("https://github.com/user/repo.git", "", "https://github.com/user/repo.git")]
    [InlineData("https://github.com/user/repo.git", "   ", "https://github.com/user/repo.git")]
    [InlineData("https://github.com/user/repo.git", "ghp_abc123", "https://ghp_abc123@github.com/user/repo.git")]
    [InlineData("https://dev.azure.com/org/repo.git", "pat-token", "https://pat-token@dev.azure.com/org/repo.git")]
    [InlineData("git@github.com:user/repo.git", "ghp_abc123", "git@github.com:user/repo.git")]
    public void BuildAuthenticatedUrl_ReturnsCorrectUrl(string repoUrl, string? token, string expected)
    {
        var result = RepositoryManager.BuildAuthenticatedUrl(repoUrl, token);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildAuthenticatedUrl_HttpUrl_InsertsTokenCorrectly()
    {
        var result = RepositoryManager.BuildAuthenticatedUrl(
            "http://gitserver.local/repo.git", "mytoken");

        Assert.Equal("http://mytoken@gitserver.local/repo.git", result);
    }
}
