using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Models;
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

    [McpServerTool, Description("Capture a new item into the authenticated user's inbox for later processing.")]
    public static async Task<string> capture_inbox(
        ClaimsPrincipal user,
        IInboxService inboxService,
        [Description("The title of the inbox item (max 300 characters).")] string title,
        [Description("Optional description of the inbox item (max 4000 characters).")] string? description = null)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        if (string.IsNullOrWhiteSpace(title))
        {
            return JsonSerializer.Serialize(new { error = "Title is required." }, JsonOptions);
        }

        if (title.Length > 300)
        {
            return JsonSerializer.Serialize(new { error = "Title must be 300 characters or fewer." }, JsonOptions);
        }

        if (description != null && description.Length > 4000)
        {
            return JsonSerializer.Serialize(new { error = "Description must be 4000 characters or fewer." }, JsonOptions);
        }

        var result = await inboxService.AddItem(title, description, userId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var item = result.Data!;
        var response = new
        {
            item.Id,
            item.Title,
            item.Description,
            createdAt = item.CreatedAt.ToString("O")
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    [McpServerTool, Description("Process an inbox item by converting it to a task in a project.")]
    public static async Task<string> process_inbox_item(
        ClaimsPrincipal user,
        IInboxService inboxService,
        [Description("The ID of the inbox item to process.")] int inboxItemId,
        [Description("The ID of the project to create the task in.")] int projectId,
        [Description("The priority for the new task: Low, Medium, High, or Critical.")] string priority,
        [Description("Optional. Set to true to mark the task as a next action.")] bool isNextAction = false,
        [Description("Optional. Set to true to mark the task as someday/maybe.")] bool isSomedayMaybe = false)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        if (!Enum.TryParse<TaskItemPriority>(priority, ignoreCase: true, out var parsedPriority))
        {
            return JsonSerializer.Serialize(new { error = $"Invalid priority value: '{priority}'. Valid values are: Low, Medium, High, Critical." }, JsonOptions);
        }

        var result = await inboxService.ConvertToTask(inboxItemId, projectId, parsedPriority, null, isNextAction, isSomedayMaybe, userId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var task = result.Data!;
        var response = new
        {
            task.Id,
            task.Title,
            task.Description,
            status = task.Status.ToString(),
            priority = task.Priority.ToString(),
            task.ProjectId,
            task.IsNextAction,
            task.IsSomedayMaybe,
            createdAt = task.CreatedAt.ToString("O")
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }
}
