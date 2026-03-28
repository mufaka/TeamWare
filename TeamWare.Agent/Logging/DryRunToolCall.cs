namespace TeamWare.Agent.Logging;

/// <summary>
/// Represents a tool call that was intercepted and logged during dry run mode.
/// </summary>
public class DryRunToolCall
{
    /// <summary>
    /// The kind of tool call (e.g., "Shell", "MCP", "Write").
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Details about the tool call (e.g., the command text, tool name, or filename).
    /// </summary>
    public required string Details { get; init; }

    /// <summary>
    /// The LLM's stated intention for the tool call, if available.
    /// </summary>
    public string? Intention { get; init; }
}
