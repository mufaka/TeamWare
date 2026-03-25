using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Tools;

[McpServerToolType]
[Authorize(AuthenticationSchemes = TeamWare.Web.Authentication.PatAuthenticationHandler.SchemeName)]
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
            isOverdue = t.DueDate.HasValue && t.DueDate.Value.Date < DateTime.UtcNow.Date && t.Status != TaskItemStatus.Done,
            t.IsNextAction
        });

        return JsonSerializer.Serialize(tasks, JsonOptions);
    }

    [McpServerTool, Description("Create a new task in a project.")]
    public static async Task<string> create_task(
        ClaimsPrincipal user,
        ITaskService taskService,
        [Description("The ID of the project to create the task in.")] int projectId,
        [Description("The title of the task (max 300 characters).")] string title,
        [Description("Optional description of the task (max 4000 characters).")] string? description = null,
        [Description("Optional priority: Low, Medium, High, or Critical. Defaults to Medium.")] string? priority = null,
        [Description("Optional due date in ISO 8601 format (e.g. 2025-12-31).")] string? dueDate = null)
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

        var parsedPriority = TaskItemPriority.Medium;
        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (!Enum.TryParse<TaskItemPriority>(priority, ignoreCase: true, out parsedPriority))
            {
                return JsonSerializer.Serialize(new { error = $"Invalid priority value: '{priority}'. Valid values are: Low, Medium, High, Critical." }, JsonOptions);
            }
        }

        DateTime? parsedDueDate = null;
        if (!string.IsNullOrWhiteSpace(dueDate))
        {
            if (!DateTime.TryParse(dueDate, out var parsed))
            {
                return JsonSerializer.Serialize(new { error = $"Invalid due date format: '{dueDate}'. Use ISO 8601 format (e.g. 2025-12-31)." }, JsonOptions);
            }
            parsedDueDate = parsed;
        }

        var result = await taskService.CreateTask(projectId, title, description, parsedPriority, parsedDueDate, userId);

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
            dueDate = task.DueDate?.ToString("O"),
            task.ProjectId,
            createdAt = task.CreatedAt.ToString("O")
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    [McpServerTool, Description("Update the status of a task.")]
    public static async Task<string> update_task_status(
        ClaimsPrincipal user,
        ITaskService taskService,
        [Description("The ID of the task to update.")] int taskId,
        [Description("The new status: ToDo, InProgress, InReview, or Done.")] string status)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        if (!Enum.TryParse<TaskItemStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            return JsonSerializer.Serialize(new { error = $"Invalid status value: '{status}'. Valid values are: ToDo, InProgress, InReview, Done." }, JsonOptions);
        }

        var result = await taskService.ChangeStatus(taskId, parsedStatus, userId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var task = result.Data!;
        var response = new
        {
            task.Id,
            task.Title,
            status = task.Status.ToString(),
            priority = task.Priority.ToString(),
            dueDate = task.DueDate?.ToString("O"),
            task.ProjectId,
            updatedAt = task.UpdatedAt.ToString("O")
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    [McpServerTool, Description("Assign users to a task.")]
    public static async Task<string> assign_task(
        ClaimsPrincipal user,
        ITaskService taskService,
        [Description("The ID of the task to assign users to.")] int taskId,
        [Description("Array of user IDs to assign to the task.")] string[] userIds)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        if (userIds.Length == 0)
        {
            return JsonSerializer.Serialize(new { error = "At least one user ID is required." }, JsonOptions);
        }

        var result = await taskService.AssignMembers(taskId, userIds, userId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        return JsonSerializer.Serialize(new { success = true, message = $"Successfully assigned {userIds.Length} user(s) to task {taskId}." }, JsonOptions);
    }

    [McpServerTool, Description("Add a comment to a task.")]
    public static async Task<string> add_comment(
        ClaimsPrincipal user,
        ICommentService commentService,
        [Description("The ID of the task to add a comment to.")] int taskId,
        [Description("The comment content (max 4000 characters).")] string content)
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

        var result = await commentService.AddComment(taskId, content, userId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var comment = result.Data!;
        var response = new
        {
            comment.Id,
            comment.TaskItemId,
            comment.Content,
            authorId = comment.AuthorId,
            createdAt = comment.CreatedAt.ToString("O")
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }
}
