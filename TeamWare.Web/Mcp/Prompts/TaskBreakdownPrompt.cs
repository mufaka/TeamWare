using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Prompts;

[McpServerPromptType]
[Authorize]
public class TaskBreakdownPrompt
{
    [McpServerPrompt, Description("Generates a prompt to break down a task description into 3-7 actionable subtasks, considering the project's existing tasks to avoid duplication.")]
    public static async Task<IEnumerable<ChatMessage>> task_breakdown(
        ClaimsPrincipal user,
        ITaskService taskService,
        [Description("The ID of the project the task belongs to.")] int projectId,
        [Description("A description of the task or feature to break down into subtasks.")] string taskDescription)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var tasksResult = await taskService.GetTasksForProject(projectId, userId);
        if (!tasksResult.Succeeded)
        {
            return [new(ChatRole.System, $"Error: {string.Join("; ", tasksResult.Errors)}")];
        }

        var existingTasks = tasksResult.Data!;

        var systemSb = new StringBuilder();
        systemSb.AppendLine("You are a project planning assistant. The user will describe a task or feature.");
        systemSb.AppendLine("Suggest 3-7 actionable subtasks that break down the work into manageable pieces.");
        systemSb.AppendLine();
        systemSb.AppendLine("Guidelines:");
        systemSb.AppendLine("- Each subtask should be specific and actionable.");
        systemSb.AppendLine("- Avoid duplicating any of the existing tasks listed below.");
        systemSb.AppendLine("- Consider dependencies between subtasks and suggest a logical order.");
        systemSb.AppendLine("- Keep subtask titles concise (under 100 characters).");
        systemSb.AppendLine();

        if (existingTasks.Count > 0)
        {
            systemSb.AppendLine($"## Existing Tasks in Project ({existingTasks.Count})");
            foreach (var task in existingTasks)
            {
                systemSb.AppendLine($"- [{task.Status}] {task.Title}");
            }
        }
        else
        {
            systemSb.AppendLine("The project currently has no tasks.");
        }

        return
        [
            new(ChatRole.System, systemSb.ToString().TrimEnd()),
            new(ChatRole.User, $"Please break down the following into subtasks:\n\n{taskDescription}")
        ];
    }
}
