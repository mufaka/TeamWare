namespace TeamWare.Agent.Configuration;

/// <summary>
/// The result of resolving a repository for a given task/project.
/// Contains everything needed to clone/pull and set the Copilot CWD.
/// </summary>
public record ResolvedRepository(
    string? RepositoryUrl,
    string Branch,
    string? AccessToken,
    string WorkingDirectory);
