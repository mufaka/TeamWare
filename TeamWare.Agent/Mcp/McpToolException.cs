namespace TeamWare.Agent.Mcp;

public class McpToolException : Exception
{
    public string ToolName { get; }

    public McpToolException(string toolName, string message)
        : base($"MCP tool '{toolName}' failed: {message}")
    {
        ToolName = toolName;
    }

    public McpToolException(string toolName, string message, Exception innerException)
        : base($"MCP tool '{toolName}' failed: {message}", innerException)
    {
        ToolName = toolName;
    }
}
