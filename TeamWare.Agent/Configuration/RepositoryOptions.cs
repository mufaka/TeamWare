namespace TeamWare.Agent.Configuration;

/// <summary>
/// Maps a TeamWare project to its source code repository.
/// When the agent picks up a task belonging to the named project,
/// it clones/pulls this repository into a project-specific subdirectory
/// under the agent's WorkingDirectory.
/// </summary>
public class RepositoryOptions
{
    /// <summary>
    /// The TeamWare project name this repository is associated with.
    /// Matched case-insensitively against AgentTask.ProjectName.
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Git repository URL (HTTPS or SSH).
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Branch to clone/pull (default: "main").
    /// </summary>
    public string Branch { get; set; } = "main";

    /// <summary>
    /// Optional access token for private repositories.
    /// Inserted into the HTTPS URL for authentication.
    /// </summary>
    public string? AccessToken { get; set; }
}
