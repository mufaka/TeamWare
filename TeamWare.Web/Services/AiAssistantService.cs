using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class AiAssistantService : IAiAssistantService
{
    private readonly IOllamaService _ollamaService;
    private readonly IActivityLogService _activityLogService;
    private readonly IProgressService _progressService;
    private readonly ITaskService _taskService;
    private readonly IInboxService _inboxService;
    private readonly IProjectService _projectService;

    public AiAssistantService(
        IOllamaService ollamaService,
        IActivityLogService activityLogService,
        IProgressService progressService,
        ITaskService taskService,
        IInboxService inboxService,
        IProjectService projectService)
    {
        _ollamaService = ollamaService;
        _activityLogService = activityLogService;
        _progressService = progressService;
        _taskService = taskService;
        _inboxService = inboxService;
        _projectService = projectService;
    }

    public Task<bool> IsAvailable()
    {
        return _ollamaService.IsConfigured();
    }

    public Task<ServiceResult<string>> RewriteProjectDescription(string description)
    {
        var systemPrompt = "You are a professional technical writer. Rewrite the following project description " +
            "to be clear, professional, and well-structured. Preserve the original meaning and all factual details. " +
            "Return only the rewritten description with no preamble, commentary, or explanation.";

        return _ollamaService.GenerateCompletion(systemPrompt, description);
    }

    public Task<ServiceResult<string>> RewriteTaskDescription(string description)
    {
        var systemPrompt = "You are a professional technical writer. Rewrite the following task description " +
            "as a clear, actionable work item. Preserve the original meaning and all requirements. " +
            "Return only the rewritten description with no preamble, commentary, or explanation.";

        return _ollamaService.GenerateCompletion(systemPrompt, description);
    }

    public Task<ServiceResult<string>> PolishComment(string comment)
    {
        var systemPrompt = "You are a professional editor. Polish the following comment for clarity and tone " +
            "while preserving the original intent. Keep the length similar to the original. " +
            "Return only the polished comment with no preamble, commentary, or explanation.";

        return _ollamaService.GenerateCompletion(systemPrompt, comment);
    }

    public Task<ServiceResult<string>> ExpandInboxItem(string title, string? description)
    {
        var systemPrompt = "You are a professional technical writer. Expand the following brief note into a " +
            "fuller description suitable for a task or work item. Add relevant detail and structure while " +
            "preserving the original intent. Return only the expanded description with no preamble, commentary, or explanation.";

        var userPrompt = string.IsNullOrWhiteSpace(description)
            ? title
            : $"{title}\n\n{description}";

        return _ollamaService.GenerateCompletion(systemPrompt, userPrompt);
    }

    public async Task<ServiceResult<string>> GenerateProjectSummary(int projectId, string userId, SummaryPeriod period)
    {
        var since = GetPeriodStart(period);

        var activity = await _activityLogService.GetActivityForProject(projectId, since);
        var stats = await _progressService.GetProjectStatistics(projectId);
        var overdue = await _progressService.GetOverdueTasks(projectId);
        var upcoming = await _progressService.GetUpcomingDeadlines(projectId);

        var userPrompt = FormatProjectSummaryData(activity, stats, overdue, upcoming, period);

        var systemPrompt = "You are a concise project manager assistant. Summarize the project activity, " +
            "current status, and any risks or action items based on the data provided. " +
            "Use short paragraphs or bullet points. Do not invent data not present in the input. " +
            "Return only the summary with no preamble, commentary, or explanation.";

        return await _ollamaService.GenerateCompletion(systemPrompt, userPrompt);
    }

    public async Task<ServiceResult<string>> GeneratePersonalDigest(string userId)
    {
        var since = DateTime.UtcNow.AddHours(-24);

        var activity = await _activityLogService.GetActivityForUser(userId, since);

        var tasksResult = await _taskService.GetWhatsNext(userId, 50);
        var tasks = tasksResult.Succeeded ? tasksResult.Data! : [];

        var userPrompt = FormatPersonalDigestData(activity, tasks);

        var systemPrompt = "You are a personal productivity assistant. Summarize what the user accomplished " +
            "in the last 24 hours and what they have coming up next. Use short paragraphs or bullet points. " +
            "Do not invent data not present in the input. " +
            "Return only the summary with no preamble, commentary, or explanation.";

        return await _ollamaService.GenerateCompletion(systemPrompt, userPrompt);
    }

    public async Task<ServiceResult<string>> GenerateReviewPreparation(string userId)
    {
        var inboxResult = await _inboxService.GetUnprocessedItems(userId);
        var inboxItems = inboxResult.Succeeded ? inboxResult.Data! : [];

        var somedayResult = await _taskService.GetSomedayMaybe(userId);
        var somedayItems = somedayResult.Succeeded ? somedayResult.Data! : [];

        var projectsResult = await _projectService.GetProjectsForUser(userId);
        var projects = projectsResult.Succeeded ? projectsResult.Data! : [];

        var allUpcoming = new List<TaskItem>();
        var allOverdue = new List<TaskItem>();
        foreach (var project in projects)
        {
            var upcoming = await _progressService.GetUpcomingDeadlines(project.Id, 14);
            allUpcoming.AddRange(upcoming);
            var overdue = await _progressService.GetOverdueTasks(project.Id);
            allOverdue.AddRange(overdue);
        }

        var staleCutoff = DateTime.UtcNow.AddDays(-14);
        var staleActivity = new List<TaskItem>();
        foreach (var project in projects)
        {
            var tasksResult = await _taskService.GetTasksForProject(project.Id, userId,
                statusFilter: null, priorityFilter: null);
            if (!tasksResult.Succeeded) continue;
            foreach (var task in tasksResult.Data!)
            {
                if (task.Status == TaskItemStatus.Done) continue;
                if (task.UpdatedAt < staleCutoff)
                {
                    staleActivity.Add(task);
                }
            }
        }

        var userPrompt = FormatReviewPreparationData(inboxItems, somedayItems, allUpcoming, allOverdue, staleActivity);

        var systemPrompt = "You are a GTD (Getting Things Done) review assistant. Help the user prepare for their " +
            "weekly review by summarizing what needs attention: unprocessed inbox items, upcoming deadlines, " +
            "overdue tasks, stale tasks, and someday/maybe items to reconsider. Use short paragraphs or bullet points. " +
            "Do not invent data not present in the input. " +
            "Return only the summary with no preamble, commentary, or explanation.";

        return await _ollamaService.GenerateCompletion(systemPrompt, userPrompt);
    }

    private static DateTime GetPeriodStart(SummaryPeriod period)
    {
        return period switch
        {
            SummaryPeriod.Today => DateTime.UtcNow.Date,
            SummaryPeriod.ThisWeek => DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek),
            SummaryPeriod.ThisMonth => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => DateTime.UtcNow.Date
        };
    }

    private static string FormatProjectSummaryData(
        List<ActivityLogEntry> activity,
        ProjectStatistics stats,
        List<TaskItem> overdue,
        List<TaskItem> upcoming,
        SummaryPeriod period)
    {
        var sb = new System.Text.StringBuilder();

        var periodLabel = period switch
        {
            SummaryPeriod.Today => "today",
            SummaryPeriod.ThisWeek => "this week",
            SummaryPeriod.ThisMonth => "this month",
            _ => "today"
        };

        sb.AppendLine($"Project summary for {periodLabel}:");
        sb.AppendLine();

        sb.AppendLine("Task Statistics:");
        sb.AppendLine($"  Total: {stats.TotalTasks}");
        sb.AppendLine($"  To Do: {stats.TaskCountToDo}");
        sb.AppendLine($"  In Progress: {stats.TaskCountInProgress}");
        sb.AppendLine($"  In Review: {stats.TaskCountInReview}");
        sb.AppendLine($"  Done: {stats.TaskCountDone}");
        sb.AppendLine($"  Blocked: {stats.TaskCountBlocked}");
        sb.AppendLine($"  Error: {stats.TaskCountError}");
        sb.AppendLine($"  Completion: {stats.CompletionPercentage}%");
        sb.AppendLine();

        if (overdue.Count > 0)
        {
            sb.AppendLine($"Overdue Tasks ({overdue.Count}):");
            foreach (var task in overdue)
            {
                sb.AppendLine($"  - {task.Title} (due {task.DueDate:yyyy-MM-dd})");
            }
            sb.AppendLine();
        }

        if (upcoming.Count > 0)
        {
            sb.AppendLine($"Upcoming Deadlines ({upcoming.Count}):");
            foreach (var task in upcoming)
            {
                sb.AppendLine($"  - {task.Title} (due {task.DueDate:yyyy-MM-dd})");
            }
            sb.AppendLine();
        }

        if (activity.Count > 0)
        {
            sb.AppendLine($"Recent Activity ({activity.Count} events):");
            foreach (var entry in activity.Take(50))
            {
                var userName = entry.User?.DisplayName ?? "Unknown";
                var taskTitle = entry.TaskItem?.Title ?? "Unknown task";
                sb.AppendLine($"  - {userName}: {entry.ChangeType} on \"{taskTitle}\" at {entry.CreatedAt:yyyy-MM-dd HH:mm}");
            }
        }
        else
        {
            sb.AppendLine("No activity recorded for this period.");
        }

        return sb.ToString();
    }

    private static string FormatPersonalDigestData(
        List<ActivityLogEntry> activity,
        List<TaskItem> whatsNext)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("Personal digest for the last 24 hours:");
        sb.AppendLine();

        if (activity.Count > 0)
        {
            sb.AppendLine($"Your Activity ({activity.Count} events):");
            foreach (var entry in activity.Take(50))
            {
                var taskTitle = entry.TaskItem?.Title ?? "Unknown task";
                sb.AppendLine($"  - {entry.ChangeType} on \"{taskTitle}\" at {entry.CreatedAt:yyyy-MM-dd HH:mm}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No activity in the last 24 hours.");
            sb.AppendLine();
        }

        if (whatsNext.Count > 0)
        {
            sb.AppendLine($"What's Next ({whatsNext.Count} tasks):");
            foreach (var task in whatsNext.Take(20))
            {
                var dueInfo = task.DueDate.HasValue ? $" (due {task.DueDate:yyyy-MM-dd})" : "";
                sb.AppendLine($"  - {task.Title} [{task.Status}] [{task.Priority}]{dueInfo}");
            }
        }
        else
        {
            sb.AppendLine("No upcoming tasks.");
        }

        return sb.ToString();
    }

    private static string FormatReviewPreparationData(
        List<InboxItem> inboxItems,
        List<TaskItem> somedayItems,
        List<TaskItem> upcoming,
        List<TaskItem> overdue,
        List<TaskItem> stale)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("GTD Weekly Review preparation data:");
        sb.AppendLine();

        sb.AppendLine($"Unprocessed Inbox Items ({inboxItems.Count}):");
        if (inboxItems.Count > 0)
        {
            foreach (var item in inboxItems)
            {
                sb.AppendLine($"  - {item.Title}");
            }
        }
        else
        {
            sb.AppendLine("  (none)");
        }
        sb.AppendLine();

        sb.AppendLine($"Overdue Tasks ({overdue.Count}):");
        if (overdue.Count > 0)
        {
            foreach (var task in overdue)
            {
                sb.AppendLine($"  - {task.Title} (due {task.DueDate:yyyy-MM-dd})");
            }
        }
        else
        {
            sb.AppendLine("  (none)");
        }
        sb.AppendLine();

        sb.AppendLine($"Upcoming Deadlines - Next 14 Days ({upcoming.Count}):");
        if (upcoming.Count > 0)
        {
            foreach (var task in upcoming)
            {
                sb.AppendLine($"  - {task.Title} (due {task.DueDate:yyyy-MM-dd})");
            }
        }
        else
        {
            sb.AppendLine("  (none)");
        }
        sb.AppendLine();

        sb.AppendLine($"Stale Tasks - No Activity in 14+ Days ({stale.Count}):");
        if (stale.Count > 0)
        {
            foreach (var task in stale)
            {
                sb.AppendLine($"  - {task.Title} [{task.Status}] (last updated {task.UpdatedAt:yyyy-MM-dd})");
            }
        }
        else
        {
            sb.AppendLine("  (none)");
        }
        sb.AppendLine();

        sb.AppendLine($"Someday/Maybe Items ({somedayItems.Count}):");
        if (somedayItems.Count > 0)
        {
            foreach (var task in somedayItems)
            {
                sb.AppendLine($"  - {task.Title}");
            }
        }
        else
        {
            sb.AppendLine("  (none)");
        }

        return sb.ToString();
    }
}
