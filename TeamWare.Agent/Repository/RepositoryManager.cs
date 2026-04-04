using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;

namespace TeamWare.Agent.Repository;

/// <summary>
/// Manages repository clone/pull operations before task processing.
/// Uses git CLI commands via Process.Start.
/// See Specification CA-50 through CA-54.
/// </summary>
public class RepositoryManager
{
    private readonly ILogger _logger;

    public RepositoryManager(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Ensures the working directory has the latest code from the configured repository.
    /// If no RepositoryUrl is configured, this is a no-op (CA-53).
    /// If the directory has no .git, clones the repository (CA-50).
    /// If the directory has .git, pulls the latest from the configured branch (CA-51).
    /// </summary>
    public async Task EnsureRepositoryAsync(AgentIdentityOptions options, CancellationToken cancellationToken)
    {
        var resolved = new ResolvedRepository(
            options.RepositoryUrl,
            options.RepositoryBranch ?? "main",
            options.RepositoryAccessToken,
            options.WorkingDirectory);

        await EnsureRepositoryAsync(resolved, options.Name, cancellationToken);
    }

    /// <summary>
    /// Ensures the working directory for a resolved repository has the latest code.
    /// If no RepositoryUrl is configured, this is a no-op (CA-53).
    /// If the directory has no .git, clones the repository (CA-50).
    /// If the directory has .git, pulls the latest from the configured branch (CA-51).
    /// </summary>
    public async Task EnsureRepositoryAsync(ResolvedRepository repo, string agentName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repo.RepositoryUrl))
        {
            _logger.LogDebug(
                "Agent '{AgentName}': No RepositoryUrl configured — skipping repository sync",
                agentName);
            return;
        }

        var gitDir = Path.Combine(repo.WorkingDirectory, ".git");

        if (Directory.Exists(gitDir))
        {
            await PullLatestAsync(repo, agentName, cancellationToken);
        }
        else
        {
            await CloneRepositoryAsync(repo, agentName, cancellationToken);
        }
    }

    private async Task CloneRepositoryAsync(ResolvedRepository repo, string agentName, CancellationToken cancellationToken)
    {
        var repoUrl = BuildAuthenticatedUrl(repo.RepositoryUrl!, repo.AccessToken);

        _logger.LogInformation(
            "Agent '{AgentName}': Cloning repository to '{WorkingDirectory}' (branch: {Branch})",
            agentName, repo.WorkingDirectory, repo.Branch);

        // Ensure parent directory exists
        var parentDir = Path.GetDirectoryName(repo.WorkingDirectory);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var args = $"clone --branch {repo.Branch} --single-branch {repoUrl} {repo.WorkingDirectory}";
        await RunGitCommandAsync(args, workingDirectory: null, agentName, cancellationToken);

        _logger.LogInformation(
            "Agent '{AgentName}': Repository cloned successfully",
            agentName);
    }

    private async Task PullLatestAsync(ResolvedRepository repo, string agentName, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Agent '{AgentName}': Pulling latest from branch '{Branch}' in '{WorkingDirectory}'",
            agentName, repo.Branch, repo.WorkingDirectory);

        // If access token is configured, update the remote URL
        if (!string.IsNullOrWhiteSpace(repo.AccessToken) &&
            !string.IsNullOrWhiteSpace(repo.RepositoryUrl))
        {
            var authenticatedUrl = BuildAuthenticatedUrl(repo.RepositoryUrl, repo.AccessToken);
            await RunGitCommandAsync(
                $"remote set-url origin {authenticatedUrl}",
                repo.WorkingDirectory,
                agentName,
                cancellationToken);
        }

        // Discard any uncommitted changes left by previous task processing (issue #284)
        await RunGitCommandAsync(
            "reset --hard HEAD",
            repo.WorkingDirectory,
            agentName,
            cancellationToken);

        await RunGitCommandAsync(
            "clean -fd",
            repo.WorkingDirectory,
            agentName,
            cancellationToken);

        // Fetch latest remote refs before checkout so origin/{branch} is up to date
        await RunGitCommandAsync(
            "fetch origin",
            repo.WorkingDirectory,
            agentName,
            cancellationToken);

        // Switch to the configured branch before pulling (issue #284).
        // Use -B to force-create/reset, handling edge cases where the local branch
        // has diverged or doesn't exist yet.
        await RunGitCommandAsync(
            $"checkout -B {repo.Branch} origin/{repo.Branch}",
            repo.WorkingDirectory,
            agentName,
            cancellationToken);

        _logger.LogInformation(
            "Agent '{AgentName}': Pull completed successfully",
            agentName);
    }

    internal virtual async Task<(int ExitCode, string Output, string Error)> RunGitCommandAsync(
        string arguments,
        string? workingDirectory,
        string agentName,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        _logger.LogDebug(
            "Agent '{AgentName}': Running git {Arguments}",
            agentName, RedactTokenFromArguments(arguments));

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var redactedError = RedactTokenFromArguments(error);
            _logger.LogError(
                "Agent '{AgentName}': git command failed (exit code {ExitCode}): {Error}",
                agentName, process.ExitCode, redactedError);

            throw new InvalidOperationException(
                $"git command failed with exit code {process.ExitCode}: {redactedError}");
        }

        return (process.ExitCode, output, error);
    }

    internal static string BuildAuthenticatedUrl(string repositoryUrl, string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return repositoryUrl;
        }

        // Insert token into HTTP(S) URL: https://github.com/... → https://token@github.com/...
        if (repositoryUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return repositoryUrl.Insert("https://".Length, $"{accessToken}@");
        }

        if (repositoryUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return repositoryUrl.Insert("http://".Length, $"{accessToken}@");
        }

        // For non-HTTP URLs (e.g., git@...), return as-is (token not applicable)
        return repositoryUrl;
    }

    private static string RedactTokenFromArguments(string text)
    {
        // Redact anything that looks like a token in a URL (https://TOKEN@...)
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(https?://)([^@]+)@",
            "$1***@");
    }
}
