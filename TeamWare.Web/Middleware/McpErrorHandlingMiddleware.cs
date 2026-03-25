using System.Text.Json;

namespace TeamWare.Web.Middleware;

/// <summary>
/// Wraps MCP requests to catch unhandled exceptions from the MCP SDK
/// (e.g., malformed JSON-RPC) and return proper JSON-RPC error responses
/// instead of 500 Internal Server Error (MCP-75).
/// </summary>
public class McpErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpErrorHandlingMiddleware> _logger;

    public McpErrorHandlingMiddleware(RequestDelegate next, ILogger<McpErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/mcp"))
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in MCP endpoint.");

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";

                    var errorResponse = new
                    {
                        jsonrpc = "2.0",
                        error = new
                        {
                            code = -32700,
                            message = "Parse error"
                        },
                        id = (object?)null
                    };

                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(errorResponse));
                }
            }

            return;
        }

        await _next(context);
    }
}
