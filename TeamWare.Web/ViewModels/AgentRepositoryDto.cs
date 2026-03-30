namespace TeamWare.Web.ViewModels;

/// <summary>
/// Read-only representation of an agent's repository mapping.
/// The <see cref="AccessToken"/> is masked or decrypted depending on context.
/// </summary>
public class AgentRepositoryDto
{
    public int Id { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Branch { get; set; } = "main";

    public string? AccessToken { get; set; }

    public int DisplayOrder { get; set; }
}
