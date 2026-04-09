namespace TeamWare.Web.ViewModels;

/// <summary>
/// Input DTO for creating or updating an agent repository mapping.
/// The <see cref="AccessToken"/> is provided in plaintext and encrypted before storage.
/// </summary>
/// <remarks>
/// Secret field semantics for <see cref="AccessToken"/>:
/// <list type="bullet">
/// <item><see cref="ClearAccessToken"/> = true → set stored token to null.</item>
/// <item><see cref="ClearAccessToken"/> = false and <see cref="AccessToken"/> is blank → keep the existing encrypted value unchanged.</item>
/// <item><see cref="ClearAccessToken"/> = false and <see cref="AccessToken"/> is non-blank → encrypt and store the new value.</item>
/// </list>
/// </remarks>
public class SaveAgentRepositoryDto
{
    public string ProjectName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Branch { get; set; } = "main";

    public string? AccessToken { get; set; }

    /// <summary>When true, explicitly clears the stored access token regardless of <see cref="AccessToken"/>.</summary>
    public bool ClearAccessToken { get; set; }

    public int DisplayOrder { get; set; }
}
