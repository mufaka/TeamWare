using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Prompts;

[McpServerPromptType]
[Authorize(AuthenticationSchemes = TeamWare.Web.Authentication.PatAuthenticationHandler.SchemeName)]
public class StandupPrompt
{
    [McpServerPrompt, Description("Generates a standup report template populated with the user's activity from the last 24 hours, formatted as Yesterday/Today/Blockers.")]
    public static async Task<IEnumerable<ChatMessage>> standup(
        ClaimsPrincipal user,
        IActivityLogService activityLogService,
        ITaskService taskService)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var since = DateTime.UtcNow.AddHours(-24);
        var activity = await activityLogService.GetActivityForUser(userId, since);

        var assignmentsResult = await taskService.GetWhatsNext(userId);
        var nextActions = assignmentsResult.Succeeded ? assignmentsResult.Data! : [];

        var sb = new StringBuilder();
        sb.AppendLine("## Yesterday (last 24 hours)");
        if (activity.Count > 0)
        {
            foreach (var entry in activity)
            {
                var taskTitle = entry.TaskItem?.Title ?? "unknown task";
                sb.AppendLine($"- {entry.ChangeType} on \"{taskTitle}\"");
            }
        }
        else
        {
            sb.AppendLine("- No recorded activity in the last 24 hours.");
        }
        sb.AppendLine();

        sb.AppendLine("## Today");
        if (nextActions.Count > 0)
        {
            sb.AppendLine("My upcoming next actions:");
            foreach (var task in nextActions)
            {
                var projectName = task.Project?.Name ?? "Unknown Project";
                var duePart = task.DueDate.HasValue ? $" (due {task.DueDate.Value:yyyy-MM-dd})" : "";
                sb.AppendLine($"- [{projectName}] {task.Title}{duePart}");
            }
        }
        else
        {
            sb.AppendLine("- No next actions assigned.");
        }
        sb.AppendLine();

        sb.AppendLine("## Blockers");
        sb.AppendLine("- (List any blockers here)");

        return [new(ChatRole.User, sb.ToString().TrimEnd())];
    }
}
