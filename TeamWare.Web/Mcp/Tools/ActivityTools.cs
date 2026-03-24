using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Tools;

[McpServerToolType]
[Authorize]
public class ActivityTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool, Description("Get activity log entries for a project or the authenticated user, filtered by time period.")]
    public static async Task<string> get_activity(
        ClaimsPrincipal user,
        IActivityLogService activityLogService,
        IProjectMemberService projectMemberService,
        [Description("Optional project ID to filter activity by project. If omitted, returns activity for the authenticated user across all projects.")] int? projectId = null,
        [Description("Time period to retrieve activity for: today, this_week, or this_month. Defaults to this_week.")] string? period = null)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var since = ParsePeriod(period);

        if (projectId.HasValue)
        {
            var memberIds = await projectMemberService.GetMemberUserIds(projectId.Value);
            if (!memberIds.Contains(userId))
            {
                return JsonSerializer.Serialize(new { error = "You are not a member of this project." }, JsonOptions);
            }

            var entries = await activityLogService.GetActivityForProject(projectId.Value, since);
            return SerializeActivityEntries(entries);
        }
        else
        {
            var entries = await activityLogService.GetActivityForUser(userId, since);
            return SerializeActivityEntries(entries);
        }
    }

    [McpServerTool, Description("Get a summary of a project including task statistics and activity counts for a given period.")]
    public static async Task<string> get_project_summary(
        ClaimsPrincipal user,
        IProgressService progressService,
        IActivityLogService activityLogService,
        IProjectMemberService projectMemberService,
        [Description("The ID of the project to summarize.")] int projectId,
        [Description("Time period for activity counts: today, this_week, or this_month. Defaults to this_week.")] string? period = null)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var memberIds = await projectMemberService.GetMemberUserIds(projectId);
        if (!memberIds.Contains(userId))
        {
            return JsonSerializer.Serialize(new { error = "You are not a member of this project." }, JsonOptions);
        }

        var since = ParsePeriod(period);
        var stats = await progressService.GetProjectStatistics(projectId);
        var overdueTasks = await progressService.GetOverdueTasks(projectId);
        var activityEntries = await activityLogService.GetActivityForProject(projectId, since);

        var completedInPeriod = activityEntries.Count(e =>
            e.ChangeType == Models.ActivityChangeType.StatusChanged &&
            e.NewValue == Models.TaskItemStatus.Done.ToString());

        var createdInPeriod = activityEntries.Count(e =>
            e.ChangeType == Models.ActivityChangeType.Created);

        var response = new
        {
            taskStatistics = new
            {
                stats.TotalTasks,
                stats.TaskCountToDo,
                stats.TaskCountInProgress,
                stats.TaskCountInReview,
                stats.TaskCountDone
            },
            completionPercentage = stats.CompletionPercentage,
            overdueCount = overdueTasks.Count,
            completedInPeriod,
            createdInPeriod
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static DateTime ParsePeriod(string? period)
    {
        return (period?.ToLowerInvariant()) switch
        {
            "today" => DateTime.UtcNow.Date,
            "this_month" => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek), // this_week (default)
        };
    }

    private static string SerializeActivityEntries(IEnumerable<Models.ActivityLogEntry> entries)
    {
        var activity = entries.Select(e => new
        {
            timestamp = e.CreatedAt.ToString("O"),
            user = e.User?.DisplayName,
            changeType = e.ChangeType.ToString(),
            taskTitle = e.TaskItem?.Title,
            e.OldValue,
            e.NewValue
        });

        return JsonSerializer.Serialize(activity, JsonOptions);
    }
}
