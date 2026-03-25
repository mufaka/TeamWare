using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Resources;

[McpServerResourceType]
[Authorize(AuthenticationSchemes = TeamWare.Web.Authentication.PatAuthenticationHandler.SchemeName)]
public class DashboardResource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerResource(UriTemplate = "teamware://dashboard", Name = "Dashboard", MimeType = "application/json")]
    [Description("Returns the authenticated user's dashboard summary including assigned task count, unread notifications, unprocessed inbox items, and upcoming deadlines.")]
    public static async Task<string> GetDashboard(
        ClaimsPrincipal user,
        ITaskService taskService,
        INotificationService notificationService,
        IInboxService inboxService,
        IProgressService progressService,
        IProjectService projectService)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var whatsNextResult = await taskService.GetWhatsNext(userId);
        var assignedTaskCount = whatsNextResult.Succeeded ? whatsNextResult.Data!.Count : 0;

        var unreadNotificationCount = await notificationService.GetUnreadCount(userId);

        var unprocessedResult = await inboxService.GetUnprocessedCount(userId);
        var unprocessedInboxCount = unprocessedResult.Succeeded ? unprocessedResult.Data : 0;

        // Gather upcoming deadlines across all user projects
        var projectsResult = await projectService.GetProjectsForUser(userId);
        var upcomingDeadlines = new List<object>();

        if (projectsResult.Succeeded)
        {
            foreach (var project in projectsResult.Data!)
            {
                var deadlines = await progressService.GetUpcomingDeadlines(project.Id);
                foreach (var task in deadlines)
                {
                    upcomingDeadlines.Add(new
                    {
                        taskId = task.Id,
                        title = task.Title,
                        projectName = project.Name,
                        dueDate = task.DueDate?.ToString("O")
                    });
                }
            }
        }

        var dashboard = new
        {
            assignedTaskCount,
            unreadNotificationCount,
            unprocessedInboxCount,
            upcomingDeadlines
        };

        return JsonSerializer.Serialize(dashboard, JsonOptions);
    }
}
