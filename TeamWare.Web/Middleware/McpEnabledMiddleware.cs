using TeamWare.Web.Services;

namespace TeamWare.Web.Middleware;

public class McpEnabledMiddleware
{
    private readonly RequestDelegate _next;

    public McpEnabledMiddleware(RequestDelegate next)
    {
        _next = next;
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
        }

        await _next(context);
    }
}
