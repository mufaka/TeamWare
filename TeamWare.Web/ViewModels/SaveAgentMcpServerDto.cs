namespace TeamWare.Web.ViewModels;

/// <summary>
/// Input DTO for creating or updating an agent MCP server connection.
/// Sensitive fields (<see cref="AuthHeader"/>, <see cref="Env"/>) are provided in plaintext
/// and encrypted before storage.
/// </summary>
public class SaveAgentMcpServerDto
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? AuthHeader { get; set; }

    public string? Command { get; set; }

    public string? Args { get; set; }

    public string? Env { get; set; }

    public int DisplayOrder { get; set; }
}
