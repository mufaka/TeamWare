namespace TeamWare.Web.ViewModels;

/// <summary>
/// Input DTO for creating or updating an agent MCP server connection.
/// Sensitive fields (<see cref="AuthHeader"/>, <see cref="Env"/>) are provided in plaintext
/// and encrypted before storage.
/// </summary>
/// <remarks>
/// Secret field semantics apply to both <see cref="AuthHeader"/> and <see cref="Env"/>:
/// <list type="bullet">
/// <item>Corresponding Clear flag = true → set stored value to null.</item>
/// <item>Corresponding Clear flag = false and field is blank → keep the existing encrypted value unchanged.</item>
/// <item>Corresponding Clear flag = false and field is non-blank → encrypt and store the new value.</item>
/// </list>
/// </remarks>
public class SaveAgentMcpServerDto
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? AuthHeader { get; set; }

    /// <summary>When true, explicitly clears the stored auth header regardless of <see cref="AuthHeader"/>.</summary>
    public bool ClearAuthHeader { get; set; }

    public string? Command { get; set; }

    public string? Args { get; set; }

    public string? Env { get; set; }

    /// <summary>When true, explicitly clears the stored environment variables regardless of <see cref="Env"/>.</summary>
    public bool ClearEnv { get; set; }

    public int DisplayOrder { get; set; }
}
