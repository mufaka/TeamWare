namespace TeamWare.Web.ViewModels;

/// <summary>
/// Read-only representation of an agent's MCP server connection.
/// Sensitive fields (<see cref="AuthHeader"/>, <see cref="Env"/>) are masked or decrypted depending on context.
/// </summary>
public class AgentMcpServerDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? AuthHeader { get; set; }

    public string? Command { get; set; }

    public string? Args { get; set; }

    public string? Env { get; set; }

    public int DisplayOrder { get; set; }
}
