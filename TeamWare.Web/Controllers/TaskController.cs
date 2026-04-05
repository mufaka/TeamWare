using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Hubs;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class TaskController : Controller
{
    private readonly ITaskService _taskService;
    private readonly IProjectService _projectService;
    private readonly IProjectMemberService _memberService;
    private readonly IActivityLogService _activityLogService;
    private readonly ICommentService _commentService;
    private readonly IAttachmentService _attachmentService;
    private readonly IHubContext<TaskHub> _taskHub;
    private readonly ApplicationDbContext _dbContext;

    public TaskController(
        ITaskService taskService,
        IProjectService projectService,
        IProjectMemberService memberService,
        IActivityLogService activityLogService,
        ICommentService commentService,
        IAttachmentService attachmentService,
        IHubContext<TaskHub> taskHub,
        ApplicationDbContext dbContext)
    {
        _taskService = taskService;
        _projectService = projectService;
        _memberService = memberService;
        _activityLogService = activityLogService;
        _commentService = commentService;
        _attachmentService = attachmentService;
        _taskHub = taskHub;
        _dbContext = dbContext;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<string> GetDisplayNameAsync(string userId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        return user?.DisplayName ?? "Unknown";
    }

    private async Task BroadcastTaskUpdatedAsync(int taskId, string[] sections, string summary)
    {
        await _taskHub.Clients.Group(TaskHub.GetGroupName(taskId))
            .SendAsync("TaskUpdated", new { taskId, sections, summary });
    }

    private async Task<List<CommentViewModel>> BuildCommentViewModels(ServiceResult<List<Comment>> commentsResult, string userId)
    {
        if (!commentsResult.Succeeded) return [];

        var viewModels = new List<CommentViewModel>();
        foreach (var c in commentsResult.Data!)
        {
            var attachmentsResult = await _attachmentService.GetAttachmentsAsync(AttachmentEntityType.Comment, c.Id);
            viewModels.Add(new CommentViewModel
            {
                Id = c.Id,
                TaskItemId = c.TaskItemId,
                AuthorId = c.AuthorId,
                AuthorDisplayName = c.Author.DisplayName,
                IsAuthorAgent = c.Author.IsAgent,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                CanEditOrDelete = c.AuthorId == userId,
                Attachments = new AttachmentListViewModel
                {
                    EntityType = AttachmentEntityType.Comment,
                    EntityId = c.Id,
                    UploadUrl = Url.Action("UploadAttachment", "Comment", new { commentId = c.Id })!,
                    DownloadUrlTemplate = Url.Action("DownloadAttachment", "Comment", new { commentId = c.Id, attachmentId = "__ID__" })!,
                    DeleteUrlTemplate = Url.Action("DeleteAttachment", "Comment", new { commentId = c.Id, attachmentId = "__ID__" })!,
                    CanUpload = true,
                    Attachments = attachmentsResult.Succeeded
                        ? attachmentsResult.Data!.Select(a => new AttachmentViewModel
                        {
                            Id = a.Id,
                            FileName = a.FileName,
                            ContentType = a.ContentType,
                            FileSizeBytes = a.FileSizeBytes,
                            UploadedByDisplayName = a.UploadedByUser?.DisplayName ?? string.Empty,
                            UploadedAt = a.UploadedAt,
                            CanDelete = a.UploadedByUserId == userId || c.AuthorId == userId
                        }).ToList()
                        : []
                }
            });
        }

        return viewModels;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int projectId, TaskItemStatus? status, TaskItemPriority? priority,
        string? assignee, string? sortBy, bool sortDesc = false, string? q = null)
    {
        var userId = GetUserId();

        var dashResult = await _projectService.GetProjectDashboard(projectId, userId);
        if (!dashResult.Succeeded)
        {
            TempData["ErrorMessage"] = dashResult.Errors.FirstOrDefault();
            return RedirectToAction("Index", "Project");
        }

        var membersResult = await _memberService.GetMembers(projectId, userId);

        ServiceResult<List<TaskItem>> tasksResult;
        if (!string.IsNullOrWhiteSpace(q))
        {
            tasksResult = await _taskService.SearchTasks(projectId, q, userId);
        }
        else
        {
            tasksResult = await _taskService.GetTasksForProject(projectId, userId,
                status, priority, assignee, sortBy, sortDesc);
        }

        if (!tasksResult.Succeeded)
        {
            TempData["ErrorMessage"] = tasksResult.Errors.FirstOrDefault();
            return RedirectToAction("Details", "Project", new { id = projectId });
        }

        var isOwnerOrAdmin = dashResult.Data!.Project.Members
            .Any(m => m.UserId == userId && (m.Role == ProjectRole.Owner || m.Role == ProjectRole.Admin));

        var viewModel = new TaskListViewModel
        {
            ProjectId = projectId,
            ProjectName = dashResult.Data.Project.Name,
            StatusFilter = status,
            PriorityFilter = priority,
            AssigneeFilter = assignee,
            SortBy = sortBy,
            SortDescending = sortDesc,
            SearchQuery = q,
            CanDeleteTasks = isOwnerOrAdmin,
            ProjectMembers = membersResult.Succeeded
                ? membersResult.Data!.Select(m => new ProjectMemberViewModel
                {
                    UserId = m.UserId,
                    DisplayName = m.User.DisplayName,
                    Email = m.User.Email ?? string.Empty,
                    Role = m.Role
                }).ToList()
                : new(),
            Tasks = tasksResult.Data!.Select(t => new TaskListItemViewModel
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status,
                Priority = t.Priority,
                DueDate = t.DueDate,
                IsNextAction = t.IsNextAction,
                IsSomedayMaybe = t.IsSomedayMaybe,
                AssigneeNames = t.Assignments.Select(a => a.User.DisplayName).ToList()
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int projectId)
    {
        var userId = GetUserId();
        var dashResult = await _projectService.GetProjectDashboard(projectId, userId);
        if (!dashResult.Succeeded)
        {
            TempData["ErrorMessage"] = dashResult.Errors.FirstOrDefault();
            return RedirectToAction("Index", "Project");
        }

        var viewModel = new CreateTaskViewModel
        {
            ProjectId = projectId,
            ProjectName = dashResult.Data!.Project.Name
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTaskViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _taskService.CreateTask(model.ProjectId, model.Title, model.Description,
            model.Priority, model.DueDate, GetUserId());

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        TempData["SuccessMessage"] = "Task created successfully.";
        return RedirectToAction(nameof(Details), new { id = result.Data!.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = GetUserId();
        var result = await _taskService.GetTask(id, userId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction("Index", "Project");
        }

        var task = result.Data!;
        var viewModel = new EditTaskViewModel
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            ProjectName = task.Project.Name,
            Title = task.Title,
            Description = task.Description,
            Priority = task.Priority,
            DueDate = task.DueDate
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditTaskViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _taskService.UpdateTask(model.Id, model.Title, model.Description,
            model.Priority, model.DueDate, GetUserId());

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        TempData["SuccessMessage"] = "Task updated successfully.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var userId = GetUserId();
        var result = await _taskService.GetTask(id, userId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction("Index", "Project");
        }

        var task = result.Data!;
        var membersResult = await _memberService.GetMembers(task.ProjectId, userId);
        var isOwnerOrAdmin = await IsOwnerOrAdmin(task.ProjectId, userId);
        var activityHistory = await _activityLogService.GetActivityForTask(id);
        var commentsResult = await _commentService.GetCommentsForTask(id, userId);
        var assignableAgentIds = await _dbContext.AgentTaskAssignmentPermissions
            .Where(p => p.AllowedAssignerUserId == userId)
            .Select(p => p.AgentUserId)
            .ToListAsync();
        var assignableAgentIdSet = assignableAgentIds.ToHashSet(StringComparer.Ordinal);

        var viewModel = new TaskDetailViewModel
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            ProjectName = task.Project.Name,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            DueDate = task.DueDate,
            IsNextAction = task.IsNextAction,
            IsSomedayMaybe = task.IsSomedayMaybe,
            CreatedByName = task.CreatedBy.DisplayName,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            CanDelete = isOwnerOrAdmin,
            CurrentUserId = userId,
            ActivityHistory = activityHistory.Select(a => new ActivityLogEntryViewModel
            {
                Id = a.Id,
                TaskItemId = a.TaskItemId,
                TaskTitle = task.Title,
                UserDisplayName = a.User.DisplayName,
                IsUserAgent = a.User.IsAgent,
                ChangeType = a.ChangeType,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                CreatedAt = a.CreatedAt
            }).ToList(),
            Comments = await BuildCommentViewModels(commentsResult, userId),
            Assignees = task.Assignments.Select(a => new TaskAssigneeViewModel
            {
                UserId = a.UserId,
                DisplayName = a.User.DisplayName,
                IsAgent = a.User.IsAgent
            }).ToList(),
            ProjectMembers = membersResult.Succeeded
                ? membersResult.Data!.Select(m => new ProjectMemberViewModel
                {
                    UserId = m.UserId,
                    DisplayName = m.User.DisplayName,
                    Email = m.User.Email ?? string.Empty,
                    Role = m.Role,
                    IsAgent = m.User.IsAgent,
                    CanReceiveTaskAssignment = !m.User.IsAgent || assignableAgentIdSet.Contains(m.UserId)
                }).ToList()
                : new()
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> StatusPartial(int id)
    {
        var userId = GetUserId();
        var result = await _taskService.GetTask(id, userId);
        if (!result.Succeeded) return NotFound();

        var task = result.Data!;
        var viewModel = new TaskDetailViewModel
        {
            Status = task.Status,
            Priority = task.Priority,
            IsNextAction = task.IsNextAction,
            IsSomedayMaybe = task.IsSomedayMaybe
        };

        return PartialView("_StatusSection", viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> ChangeStatusPartial(int id)
    {
        var userId = GetUserId();
        var result = await _taskService.GetTask(id, userId);
        if (!result.Succeeded) return NotFound();

        var task = result.Data!;
        var viewModel = new TaskDetailViewModel
        {
            Id = task.Id,
            Status = task.Status
        };

        return PartialView("_ChangeStatusSection", viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> ActivityPartial(int id)
    {
        var userId = GetUserId();
        var result = await _taskService.GetTask(id, userId);
        if (!result.Succeeded) return NotFound();

        var activityHistory = await _activityLogService.GetActivityForTask(id);
        var viewModel = activityHistory.Select(a => new ActivityLogEntryViewModel
        {
            Id = a.Id,
            TaskItemId = a.TaskItemId,
            TaskTitle = result.Data!.Title,
            UserDisplayName = a.User.DisplayName,
            IsUserAgent = a.User.IsAgent,
            ChangeType = a.ChangeType,
            OldValue = a.OldValue,
            NewValue = a.NewValue,
            CreatedAt = a.CreatedAt
        }).ToList();

        return PartialView("_ActivityHistory", viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> CommentsPartial(int id)
    {
        var userId = GetUserId();
        var result = await _taskService.GetTask(id, userId);
        if (!result.Succeeded) return NotFound();

        var commentsResult = await _commentService.GetCommentsForTask(id, userId);
        var viewModel = await BuildCommentViewModels(commentsResult, userId);

        return PartialView("_CommentList", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var task = await _taskService.GetTask(id, GetUserId());
        var projectId = task.Data?.ProjectId ?? 0;

        var result = await _taskService.DeleteTask(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Task deleted successfully.";
        }

        return RedirectToAction(nameof(Index), new { projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, TaskItemStatus status)
    {
        var result = await _taskService.ChangeStatus(id, status, GetUserId());

        if (result.Succeeded)
        {
            var displayName = await GetDisplayNameAsync(GetUserId());
            await BroadcastTaskUpdatedAsync(id, ["status", "activity"],
                $"{displayName} changed status to {status}");
        }

        if (Request.Headers["HX-Request"] == "true")
        {
            if (!result.Succeeded)
            {
                Response.Headers["HX-Reswap"] = "none";
                return BadRequest();
            }

            return PartialView("_TaskStatusBadge", result.Data!);
        }

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleNextAction(int id)
    {
        var task = await _taskService.GetTask(id, GetUserId());
        if (!task.Succeeded)
        {
            TempData["ErrorMessage"] = task.Errors.FirstOrDefault();
            return RedirectToAction("Index", "Project");
        }

        var result = task.Data!.IsNextAction
            ? await _taskService.ClearNextAction(id, GetUserId())
            : await _taskService.MarkAsNextAction(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSomedayMaybe(int id)
    {
        var task = await _taskService.GetTask(id, GetUserId());
        if (!task.Succeeded)
        {
            TempData["ErrorMessage"] = task.Errors.FirstOrDefault();
            return RedirectToAction("Index", "Project");
        }

        var result = task.Data!.IsSomedayMaybe
            ? await _taskService.ClearSomedayMaybe(id, GetUserId())
            : await _taskService.MarkAsSomedayMaybe(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, string userId)
    {
        var result = await _taskService.AssignMembers(id, [userId], GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Member assigned.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(int id, string userId)
    {
        var result = await _taskService.UnassignMembers(id, [userId], GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Member unassigned.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> IsOwnerOrAdmin(int projectId, string userId)
    {
        var dashResult = await _projectService.GetProjectDashboard(projectId, userId);
        if (!dashResult.Succeeded) return false;
        return dashResult.Data!.Project.Members
            .Any(m => m.UserId == userId && (m.Role == ProjectRole.Owner || m.Role == ProjectRole.Admin));
    }
}
