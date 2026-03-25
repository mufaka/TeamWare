using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Tools;

[McpServerToolType]
[Authorize(AuthenticationSchemes = TeamWare.Web.Authentication.PatAuthenticationHandler.SchemeName)]
public class LoungeTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool, Description("List recent messages from a project lounge or the global lounge.")]
    public static async Task<string> list_lounge_messages(
        ClaimsPrincipal user,
        ILoungeService loungeService,
        IProjectMemberService projectMemberService,
        [Description("Optional project ID. If omitted, returns messages from the global lounge.")] int? projectId = null,
        [Description("Number of messages to return (default 20, max 100).")] int count = 20)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        if (projectId.HasValue)
        {
            var memberIds = await projectMemberService.GetMemberUserIds(projectId.Value);
            if (!memberIds.Contains(userId))
            {
                return JsonSerializer.Serialize(new { error = "You are not a member of this project." }, JsonOptions);
            }
        }

        count = Math.Clamp(count, 1, 100);

        var result = await loungeService.GetMessages(projectId, null, count);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var messages = result.Data!.Select(m => new
        {
            m.Id,
            authorName = m.User?.DisplayName ?? "Unknown",
            m.Content,
            createdAt = m.CreatedAt.ToString("O")
        });

        return JsonSerializer.Serialize(messages, JsonOptions);
    }

    [McpServerTool, Description("Post a message to a project lounge or the global lounge.")]
    public static async Task<string> post_lounge_message(
        ClaimsPrincipal user,
        ILoungeService loungeService,
        IProjectMemberService projectMemberService,
        [Description("The message content (max 4000 characters).")] string content,
        [Description("Optional project ID. If omitted, posts to the global lounge.")] int? projectId = null)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        if (string.IsNullOrWhiteSpace(content))
        {
            return JsonSerializer.Serialize(new { error = "Content is required." }, JsonOptions);
        }

        if (content.Length > 4000)
        {
            return JsonSerializer.Serialize(new { error = "Content must be 4000 characters or fewer." }, JsonOptions);
        }

        if (projectId.HasValue)
        {
            var memberIds = await projectMemberService.GetMemberUserIds(projectId.Value);
            if (!memberIds.Contains(userId))
            {
                return JsonSerializer.Serialize(new { error = "You are not a member of this project." }, JsonOptions);
            }
        }

        var result = await loungeService.SendMessage(projectId, userId, content);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var message = result.Data!;
        var response = new
        {
            message.Id,
            authorName = message.User?.DisplayName ?? "Unknown",
            message.Content,
            createdAt = message.CreatedAt.ToString("O")
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    [McpServerTool, Description("Search for messages in a project lounge or the global lounge by content.")]
    public static async Task<string> search_lounge_messages(
        ClaimsPrincipal user,
        ILoungeService loungeService,
        IProjectMemberService projectMemberService,
        [Description("The search query to match against message content.")] string query,
        [Description("Optional project ID. If omitted, searches the global lounge.")] int? projectId = null)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonSerializer.Serialize(new { error = "Search query is required." }, JsonOptions);
        }

        if (projectId.HasValue)
        {
            var memberIds = await projectMemberService.GetMemberUserIds(projectId.Value);
            if (!memberIds.Contains(userId))
            {
                return JsonSerializer.Serialize(new { error = "You are not a member of this project." }, JsonOptions);
            }
        }

        // Retrieve a large batch of recent messages and filter by query
        var result = await loungeService.GetMessages(projectId, null, 100);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var matchingMessages = result.Data!
            .Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(m => new
            {
                m.Id,
                authorName = m.User?.DisplayName ?? "Unknown",
                m.Content,
                createdAt = m.CreatedAt.ToString("O")
            });

        return JsonSerializer.Serialize(matchingMessages, JsonOptions);
    }
}
