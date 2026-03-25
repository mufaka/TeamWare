using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Resources;

[McpServerResourceType]
[Authorize(AuthenticationSchemes = TeamWare.Web.Authentication.PatAuthenticationHandler.SchemeName)]
public class ProjectSummaryResource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerResource(UriTemplate = "teamware://projects/{projectId}/summary", Name = "Project Summary", MimeType = "application/json")]
    [Description("Returns a summary of a specific project including name, status, member count, task statistics, and completion percentage.")]
    public static async Task<string> GetProjectSummary(
        ClaimsPrincipal user,
        IProjectService projectService,
        IProgressService progressService,
        [Description("The ID of the project to get a summary for.")] int projectId)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var dashboardResult = await projectService.GetProjectDashboard(projectId, userId);
        if (!dashboardResult.Succeeded)
        {
            throw new McpException($"Error: {string.Join("; ", dashboardResult.Errors)}");
        }

        var dashboard = dashboardResult.Data!;
        var stats = await progressService.GetProjectStatistics(projectId);

        var summary = new
        {
            name = dashboard.Project.Name,
            status = dashboard.Project.Status.ToString(),
            memberCount = dashboard.Project.Members.Count,
            taskStats = new
            {
                total = stats.TotalTasks,
                toDo = stats.TaskCountToDo,
                inProgress = stats.TaskCountInProgress,
                inReview = stats.TaskCountInReview,
                done = stats.TaskCountDone
            },
            completionPct = stats.CompletionPercentage
        };

        return JsonSerializer.Serialize(summary, JsonOptions);
    }
}
