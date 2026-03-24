using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Tools;

[McpServerToolType]
[Authorize]
public class InboxTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool, Description("Get all unprocessed inbox items for the authenticated user.")]
    public static async Task<string> my_inbox(
        ClaimsPrincipal user,
        IInboxService inboxService)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var result = await inboxService.GetUnprocessedItems(userId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var items = result.Data!.Select(i => new
        {
            i.Id,
            i.Title,
            i.Description,
            createdAt = i.CreatedAt.ToString("O")
        });

        return JsonSerializer.Serialize(items, JsonOptions);
    }
}
