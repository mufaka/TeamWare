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
        if (string.IsNullOrWhiteSpace(options.RepositoryUrl))
        {
            _logger.LogDebug(
                "Agent '{AgentName}': No RepositoryUrl configured — skipping repository sync",
                options.Name);
            return;
        }

        var gitDir = Path.Combine(options.WorkingDirectory, ".git");

        if (Directory.Exists(gitDir))
        {
            await PullLatestAsync(options, cancellationToken);
        }
        else
        {
            await CloneRepositoryAsync(options, cancellationToken);
        }
    }

    private async Task CloneRepositoryAsync(AgentIdentityOptions options, CancellationToken cancellationToken)
    {
        var repoUrl = BuildAuthenticatedUrl(options.RepositoryUrl!, options.RepositoryAccessToken);
        var branch = options.RepositoryBranch ?? "main";

        _logger.LogInformation(
            "Agent '{AgentName}': Cloning repository to '{WorkingDirectory}' (branch: {Branch})",
            options.Name, options.WorkingDirectory, branch);

        // Ensure parent directory exists
        var parentDir = Path.GetDirectoryName(options.WorkingDirectory);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var args = $"clone --branch {branch} --single-branch {repoUrl} {options.WorkingDirectory}";
        await RunGitCommandAsync(args, workingDirectory: null, options.Name, cancellationToken);

        _logger.LogInformation(
            "Agent '{AgentName}': Repository cloned successfully",
            options.Name);
    }

    private async Task PullLatestAsync(AgentIdentityOptions options, CancellationToken cancellationToken)
    {
        var branch = options.RepositoryBranch ?? "main";

        _logger.LogInformation(
            "Agent '{AgentName}': Pulling latest from branch '{Branch}' in '{WorkingDirectory}'",
            options.Name, branch, options.WorkingDirectory);

        // If access token is configured, update the remote URL
        if (!string.IsNullOrWhiteSpace(options.RepositoryAccessToken) &&
            !string.IsNullOrWhiteSpace(options.RepositoryUrl))
        {
            var authenticatedUrl = BuildAuthenticatedUrl(options.RepositoryUrl, options.RepositoryAccessToken);
            await RunGitCommandAsync(
                $"remote set-url origin {authenticatedUrl}",
                options.WorkingDirectory,
                options.Name,
                cancellationToken);
        }

        await RunGitCommandAsync(
            $"pull origin {branch}",
            options.WorkingDirectory,
            options.Name,
            cancellationToken);

        _logger.LogInformation(
            "Agent '{AgentName}': Pull completed successfully",
            options.Name);
    }

    internal async Task<(int ExitCode, string Output, string Error)> RunGitCommandAsync(
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
