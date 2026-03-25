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
public class ProjectContextPrompt
{
    [McpServerPrompt, Description("Provides rich context about a project including description, members, task statistics, and recent activity. Use this to ground an AI conversation about a specific project.")]
    public static async Task<IEnumerable<ChatMessage>> project_context(
        ClaimsPrincipal user,
        IProjectService projectService,
        IProjectMemberService projectMemberService,
        IProgressService progressService,
        IActivityLogService activityLogService,
        [Description("The ID of the project to get context for.")] int projectId)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var dashboardResult = await projectService.GetProjectDashboard(projectId, userId);
        if (!dashboardResult.Succeeded)
        {
            return [new(ChatRole.System, $"Error: {string.Join("; ", dashboardResult.Errors)}")];
        }

        var dashboard = dashboardResult.Data!;
        var stats = await progressService.GetProjectStatistics(projectId);

        var membersResult = await projectMemberService.GetMembers(projectId, userId);
        var memberList = membersResult.Succeeded ? membersResult.Data! : [];

        var recentActivity = await activityLogService.GetActivityForProject(projectId, 10);

        var sb = new StringBuilder();
        sb.AppendLine($"# Project: {dashboard.Project.Name}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(dashboard.Project.Description))
        {
            sb.AppendLine($"## Description");
            sb.AppendLine(dashboard.Project.Description);
            sb.AppendLine();
        }

        sb.AppendLine($"## Members ({memberList.Count})");
        foreach (var member in memberList)
        {
            sb.AppendLine($"- {member.User?.DisplayName ?? member.UserId} ({member.Role})");
        }
        sb.AppendLine();

        sb.AppendLine($"## Task Statistics");
        sb.AppendLine($"- Total: {stats.TotalTasks}");
        sb.AppendLine($"- To Do: {stats.TaskCountToDo}");
        sb.AppendLine($"- In Progress: {stats.TaskCountInProgress}");
        sb.AppendLine($"- In Review: {stats.TaskCountInReview}");
        sb.AppendLine($"- Done: {stats.TaskCountDone}");
        sb.AppendLine($"- Completion: {stats.CompletionPercentage}%");
        sb.AppendLine();

        if (recentActivity.Count > 0)
        {
            sb.AppendLine($"## Recent Activity (last {recentActivity.Count} entries)");
            foreach (var entry in recentActivity)
            {
                var userName = entry.User?.DisplayName ?? entry.UserId;
                sb.AppendLine($"- [{entry.CreatedAt:O}] {userName}: {entry.ChangeType} on \"{entry.TaskItem?.Title ?? "unknown task"}\"");
            }
        }

        return [new(ChatRole.System, sb.ToString().TrimEnd())];
    }
}
