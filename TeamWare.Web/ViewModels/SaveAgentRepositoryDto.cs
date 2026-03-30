namespace TeamWare.Web.ViewModels;

/// <summary>
/// Input DTO for creating or updating an agent repository mapping.
/// The <see cref="AccessToken"/> is provided in plaintext and encrypted before storage.
/// </summary>
public class SaveAgentRepositoryDto
{
    public string ProjectName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Branch { get; set; } = "main";

    public string? AccessToken { get; set; }

    public int DisplayOrder { get; set; }
}
