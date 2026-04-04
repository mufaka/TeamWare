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

    [Fact]
    public async Task PullLatest_RunsResetCleanFetchCheckout_InOrder()
    {
        // Arrange: a testable manager that captures git commands instead of running them
        var logger = new TestLogger<RepositoryManager>();
        var manager = new TestableRepositoryManager(logger);

        var repo = new ResolvedRepository(
            RepositoryUrl: "https://github.com/user/repo.git",
            Branch: "main",
            AccessToken: "ghp_token123",
            WorkingDirectory: manager.FakeWorkingDirectory);

        // Act
        await manager.EnsureRepositoryAsync(repo, "test-agent", CancellationToken.None);

        // Assert: verify the exact command sequence
        var commands = manager.GitCommands;
        Assert.Equal(5, commands.Count);

        // 1. Update remote URL
        Assert.StartsWith("remote set-url origin https://ghp_token123@github.com/user/repo.git", commands[0]);

        // 2. Reset uncommitted changes
        Assert.Equal("reset --hard HEAD", commands[1]);

        // 3. Clean untracked files
        Assert.Equal("clean -fd", commands[2]);

        // 4. Fetch latest remote refs
        Assert.Equal("fetch origin", commands[3]);

        // 5. Checkout configured branch from remote
        Assert.Equal("checkout -B main origin/main", commands[4]);
    }

    [Fact]
    public async Task PullLatest_NoAccessToken_SkipsRemoteSetUrl()
    {
        var logger = new TestLogger<RepositoryManager>();
        var manager = new TestableRepositoryManager(logger);

        var repo = new ResolvedRepository(
            RepositoryUrl: "https://github.com/user/repo.git",
            Branch: "develop",
            AccessToken: null,
            WorkingDirectory: manager.FakeWorkingDirectory);

        await manager.EnsureRepositoryAsync(repo, "test-agent", CancellationToken.None);

        var commands = manager.GitCommands;
        Assert.Equal(4, commands.Count);

        Assert.Equal("reset --hard HEAD", commands[0]);
        Assert.Equal("clean -fd", commands[1]);
        Assert.Equal("fetch origin", commands[2]);
        Assert.Equal("checkout -B develop origin/develop", commands[3]);
    }

    [Fact]
    public async Task PullLatest_CustomBranch_UsesCorrectBranchInCheckout()
    {
        var logger = new TestLogger<RepositoryManager>();
        var manager = new TestableRepositoryManager(logger);

        var repo = new ResolvedRepository(
            RepositoryUrl: "https://github.com/user/repo.git",
            Branch: "release/v2",
            AccessToken: "token",
            WorkingDirectory: manager.FakeWorkingDirectory);

        await manager.EnsureRepositoryAsync(repo, "test-agent", CancellationToken.None);

        var commands = manager.GitCommands;
        Assert.Contains("checkout -B release/v2 origin/release/v2", commands);
        Assert.Contains("fetch origin", commands);
    }

    /// <summary>
    /// Testable subclass that captures git commands without executing them.
    /// Creates a fake .git directory so PullLatestAsync is exercised (not CloneRepositoryAsync).
    /// </summary>
    private class TestableRepositoryManager : RepositoryManager, IDisposable
    {
        private readonly string _tempDir;
        public List<string> GitCommands { get; } = [];
        public string FakeWorkingDirectory => _tempDir;

        public TestableRepositoryManager(ILogger logger) : base(logger)
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"tw-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));
        }

        internal override Task<(int ExitCode, string Output, string Error)> RunGitCommandAsync(
            string arguments,
            string? workingDirectory,
            string agentName,
            CancellationToken cancellationToken)
        {
            GitCommands.Add(arguments);
            return Task.FromResult((0, "", ""));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }
}
