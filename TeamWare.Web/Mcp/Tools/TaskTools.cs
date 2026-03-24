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
public class TaskTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool, Description("List tasks in a project with optional filtering by status, priority, or assignee.")]
    public static async Task<string> list_tasks(
        ClaimsPrincipal user,
        ITaskService taskService,
        [Description("The ID of the project to list tasks for.")] int projectId,
        [Description("Optional status filter: ToDo, InProgress, InReview, or Done.")] string? status = null,
        [Description("Optional priority filter: Low, Medium, High, or Critical.")] string? priority = null,
        [Description("Optional user ID to filter tasks by assignee.")] string? assigneeId = null)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        TaskItemStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TaskItemStatus>(status, ignoreCase: true, out var parsed))
            {
                return JsonSerializer.Serialize(new { error = $"Invalid status value: '{status}'. Valid values are: ToDo, InProgress, InReview, Done." }, JsonOptions);
            }
            statusFilter = parsed;
        }

        TaskItemPriority? priorityFilter = null;
        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (!Enum.TryParse<TaskItemPriority>(priority, ignoreCase: true, out var parsed))
            {
                return JsonSerializer.Serialize(new { error = $"Invalid priority value: '{priority}'. Valid values are: Low, Medium, High, Critical." }, JsonOptions);
            }
            priorityFilter = parsed;
        }

        var result = await taskService.GetTasksForProject(projectId, userId, statusFilter, priorityFilter, assigneeId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var tasks = result.Data!.Select(t => new
        {
            t.Id,
            t.Title,
            status = t.Status.ToString(),
            priority = t.Priority.ToString(),
            dueDate = t.DueDate?.ToString("O"),
            assignees = t.Assignments.Select(a => new
            {
                userId = a.UserId,
                displayName = a.User?.DisplayName
            })
        });

        return JsonSerializer.Serialize(tasks, JsonOptions);
    }

    [McpServerTool, Description("Get detailed information about a specific task including its comments.")]
    public static async Task<string> get_task(
        ClaimsPrincipal user,
        ITaskService taskService,
        ICommentService commentService,
        [Description("The ID of the task to retrieve.")] int taskId)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var taskResult = await taskService.GetTask(taskId, userId);

        if (!taskResult.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", taskResult.Errors) }, JsonOptions);
        }

        var task = taskResult.Data!;

        var commentsResult = await commentService.GetCommentsForTask(taskId, userId);
        var comments = commentsResult.Succeeded
            ? commentsResult.Data!.Select(c => new
            {
                c.Id,
                authorName = c.Author?.DisplayName,
                c.Content,
                createdAt = c.CreatedAt.ToString("O"),
                updatedAt = c.UpdatedAt.ToString("O")
            })
            : [];

        var response = new
        {
            task.Id,
            task.Title,
            task.Description,
            status = task.Status.ToString(),
            priority = task.Priority.ToString(),
            dueDate = task.DueDate?.ToString("O"),
            task.IsNextAction,
            task.IsSomedayMaybe,
            task.ProjectId,
            createdByUserId = task.CreatedByUserId,
            createdAt = task.CreatedAt.ToString("O"),
            updatedAt = task.UpdatedAt.ToString("O"),
            assignees = task.Assignments.Select(a => new
            {
                userId = a.UserId,
                displayName = a.User?.DisplayName
            }),
            comments
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    [McpServerTool, Description("Get the authenticated user's task assignments across all projects, prioritized by next actions and due dates.")]
    public static async Task<string> my_assignments(
        ClaimsPrincipal user,
        ITaskService taskService)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var result = await taskService.GetWhatsNext(userId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var tasks = result.Data!.Select(t => new
        {
            t.Id,
            t.Title,
            projectName = t.Project?.Name,
            t.ProjectId,
            status = t.Status.ToString(),
            priority = t.Priority.ToString(),
            dueDate = t.DueDate?.ToString("O"),
            t.IsNextAction
        });

        return JsonSerializer.Serialize(tasks, JsonOptions);
    }
}
