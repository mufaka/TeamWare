using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Tools;

[McpServerToolType]
[Authorize]
public class ProjectTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool, Description("List all projects the authenticated user is a member of.")]
    public static async Task<string> list_projects(
        ClaimsPrincipal user,
        IProjectService projectService)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var result = await projectService.GetProjectsForUser(userId);

        if (!result.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", result.Errors) }, JsonOptions);
        }

        var projects = result.Data!.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            status = p.Status.ToString(),
            memberCount = p.Members.Count
        });

        return JsonSerializer.Serialize(projects, JsonOptions);
    }

    [McpServerTool, Description("Get detailed information about a specific project including task statistics.")]
    public static async Task<string> get_project(
        ClaimsPrincipal user,
        IProjectService projectService,
        IProgressService progressService,
        [Description("The ID of the project to retrieve.")] int projectId)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var dashboardResult = await projectService.GetProjectDashboard(projectId, userId);

        if (!dashboardResult.Succeeded)
        {
            return JsonSerializer.Serialize(new { error = string.Join("; ", dashboardResult.Errors) }, JsonOptions);
        }

        var dashboard = dashboardResult.Data!;
        var stats = await progressService.GetProjectStatistics(projectId);
        var overdueTasks = await progressService.GetOverdueTasks(projectId);
        var upcomingDeadlines = await progressService.GetUpcomingDeadlines(projectId);

        var response = new
        {
            dashboard.Project.Id,
            dashboard.Project.Name,
            dashboard.Project.Description,
            status = dashboard.Project.Status.ToString(),
            createdAt = dashboard.Project.CreatedAt.ToString("O"),
            updatedAt = dashboard.Project.UpdatedAt.ToString("O"),
            totalMembers = dashboard.TotalMembers,
            taskStatistics = new
            {
                stats.TotalTasks,
                stats.TaskCountToDo,
                stats.TaskCountInProgress,
                stats.TaskCountInReview,
                stats.TaskCountDone,
                stats.CompletionPercentage
            },
            overdueTasks = overdueTasks.Select(t => new
            {
                t.Id,
                t.Title,
                priority = t.Priority.ToString(),
                dueDate = t.DueDate?.ToString("O")
            }),
            upcomingDeadlines = upcomingDeadlines.Select(t => new
            {
                t.Id,
                t.Title,
                priority = t.Priority.ToString(),
                dueDate = t.DueDate?.ToString("O")
            })
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }
}
