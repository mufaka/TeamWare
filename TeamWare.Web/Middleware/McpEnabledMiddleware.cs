using System.Text.Json;
using TeamWare.Web.Services;

namespace TeamWare.Web.Middleware;

public class McpEnabledMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpEnabledMiddleware> _logger;

    public McpEnabledMiddleware(RequestDelegate next, ILogger<McpEnabledMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/mcp"))
        {
            var configService = context.RequestServices.GetRequiredService<IGlobalConfigurationService>();
            var result = await configService.GetByKeyAsync("MCP_ENABLED");

            if (!result.Succeeded || !string.Equals(result.Data?.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Wrap MCP requests to catch unhandled exceptions from the MCP SDK
            // (e.g., malformed JSON-RPC) and return proper JSON-RPC error responses
            // instead of 500 Internal Server Error (MCP-75).
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
