using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class ProjectController : Controller
{
    private readonly IProjectService _projectService;
    private readonly IProjectMemberService _memberService;
    private readonly IActivityLogService _activityLogService;
    private readonly IProgressService _progressService;
    private readonly IProjectInvitationService _invitationService;
    private readonly IAttachmentService _attachmentService;
    private readonly IFileStorageService _fileStorageService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProjectController(
        IProjectService projectService,
        IProjectMemberService memberService,
        IActivityLogService activityLogService,
        IProgressService progressService,
        IProjectInvitationService invitationService,
        IAttachmentService attachmentService,
        IFileStorageService fileStorageService,
        UserManager<ApplicationUser> userManager)
    {
        _projectService = projectService;
        _memberService = memberService;
        _activityLogService = activityLogService;
        _progressService = progressService;
        _invitationService = invitationService;
        _attachmentService = attachmentService;
        _fileStorageService = fileStorageService;
        _userManager = userManager;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var result = await _projectService.GetProjectsForUser(GetUserId());

        var viewModel = new ProjectListViewModel
        {
            Projects = result.Data!.Select(p => new ProjectListItemViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Status = p.Status,
                MemberCount = p.Members.Count,
                UpdatedAt = p.UpdatedAt
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateProjectViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateProjectViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _projectService.CreateProject(model.Name, model.Description, GetUserId());

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        TempData["SuccessMessage"] = "Project created successfully.";
        return RedirectToAction(nameof(Details), new { id = result.Data!.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var result = await _projectService.GetProjectDashboard(id, GetUserId(), IsAdmin());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Index));
        }

        var project = result.Data!.Project;
        var viewModel = new EditProjectViewModel
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditProjectViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _projectService.UpdateProject(model.Id, model.Name, model.Description, GetUserId(), IsAdmin());

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        TempData["SuccessMessage"] = "Project updated successfully.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var userId = GetUserId();
        var result = await _projectService.GetProjectDashboard(id, userId, IsAdmin());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Index));
        }

        var dashboard = result.Data!;
        var currentUserMember = dashboard.Project.Members.FirstOrDefault(m => m.UserId == userId);

        var overdueTasks = await _progressService.GetOverdueTasks(id);
        var upcomingDeadlines = await _progressService.GetUpcomingDeadlines(id);
        var recentActivity = await _activityLogService.GetActivityForProject(id);

        var viewModel = new ProjectDashboardViewModel
        {
            Id = dashboard.Project.Id,
            Name = dashboard.Project.Name,
            Description = dashboard.Project.Description,
            Status = dashboard.Project.Status,
            TotalMembers = dashboard.TotalMembers,
            TaskCountToDo = dashboard.TaskCountToDo,
            TaskCountInProgress = dashboard.TaskCountInProgress,
            TaskCountInReview = dashboard.TaskCountInReview,
            TaskCountDone = dashboard.TaskCountDone,
            CurrentUserRole = currentUserMember?.Role ?? ProjectRole.Member,
            OverdueTaskCount = overdueTasks.Count,
            UpcomingDeadlines = upcomingDeadlines.Select(t => new TaskDeadlineViewModel
            {
                Id = t.Id,
                Title = t.Title,
                DueDate = t.DueDate!.Value,
                Status = t.Status,
                Priority = t.Priority,
                AssigneeNames = t.Assignments.Select(a => a.User.DisplayName).ToList()
            }).ToList(),
            RecentActivity = recentActivity.Select(a => new ActivityLogEntryViewModel
            {
                Id = a.Id,
                TaskItemId = a.TaskItemId,
                TaskTitle = a.TaskItem.Title,
                UserDisplayName = a.User.DisplayName,
                ChangeType = a.ChangeType,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                CreatedAt = a.CreatedAt
            }).ToList(),
            Members = dashboard.Project.Members.Select(m => new ProjectMemberViewModel
            {
                UserId = m.UserId,
                DisplayName = m.User.DisplayName,
                Email = m.User.Email ?? string.Empty,
                Role = m.Role,
                JoinedAt = m.JoinedAt
            }).OrderByDescending(m => m.Role).ThenBy(m => m.DisplayName).ToList()
        };

        if (currentUserMember != null &&
            (currentUserMember.Role == ProjectRole.Owner || currentUserMember.Role == ProjectRole.Admin))
        {
            var pendingResult = await _invitationService.GetPendingInvitationsForProject(id, userId);
            if (pendingResult.Succeeded && pendingResult.Data != null)
            {
                viewModel.PendingInvitations = pendingResult.Data.Select(i => new ProjectInvitationViewModel
                {
                    Id = i.Id,
                    ProjectId = i.ProjectId,
                    InvitedUserId = i.InvitedUserId,
                    InvitedUserDisplayName = i.InvitedUser?.DisplayName ?? string.Empty,
                    InvitedUserEmail = i.InvitedUser?.Email ?? string.Empty,
                    InvitedByUserDisplayName = i.InvitedByUser?.DisplayName ?? string.Empty,
                    Role = i.Role,
                    Status = i.Status,
                    CreatedAt = i.CreatedAt
                }).ToList();
            }
        }

        var attachmentsResult = await _attachmentService.GetAttachmentsAsync(AttachmentEntityType.Project, id);
        var isProjectOwner = currentUserMember?.Role == ProjectRole.Owner;
        viewModel.Attachments = new AttachmentListViewModel
        {
            EntityType = AttachmentEntityType.Project,
            EntityId = id,
            UploadUrl = Url.Action("UploadAttachment", "Project", new { projectId = id })!,
            DownloadUrlTemplate = Url.Action("DownloadAttachment", "Project", new { projectId = id, attachmentId = "__ID__" })!,
            DeleteUrlTemplate = Url.Action("DeleteAttachment", "Project", new { projectId = id, attachmentId = "__ID__" })!,
            CanUpload = currentUserMember != null,
            Attachments = attachmentsResult.Succeeded
                ? attachmentsResult.Data!.Select(a => new AttachmentViewModel
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    FileSizeBytes = a.FileSizeBytes,
                    UploadedByDisplayName = a.UploadedByUser?.DisplayName ?? string.Empty,
                    UploadedAt = a.UploadedAt,
                    CanDelete = a.UploadedByUserId == userId || isProjectOwner
                }).ToList()
                : []
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> ActivityTimeline(int id)
    {
        var recentActivity = await _activityLogService.GetActivityForProject(id);

        var viewModel = recentActivity.Select(a => new ActivityLogEntryViewModel
        {
            Id = a.Id,
            TaskItemId = a.TaskItemId,
            TaskTitle = a.TaskItem.Title,
            UserDisplayName = a.User.DisplayName,
            ChangeType = a.ChangeType,
            OldValue = a.OldValue,
            NewValue = a.NewValue,
            CreatedAt = a.CreatedAt
        }).ToList();

        return PartialView("_ActivityTimeline", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id)
    {
        var result = await _projectService.ArchiveProject(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Project archived successfully.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _projectService.DeleteProject(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Project deleted successfully.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> SearchUsers(int projectId, string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return Json(Array.Empty<object>());
        }

        var currentMemberIds = await _memberService.GetMemberUserIds(projectId);

        var users = _userManager.Users
            .Where(u => !currentMemberIds.Contains(u.Id) &&
                        (u.Email!.Contains(query) || u.DisplayName.Contains(query)))
            .OrderBy(u => u.DisplayName)
            .Take(10)
            .Select(u => new { u.Id, u.Email, u.DisplayName });

        return Json(await users.ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteMember(SendInvitationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please select a valid user.";
            return RedirectToAction(nameof(Details), new { id = model.ProjectId });
        }

        var role = Enum.TryParse<ProjectRole>(model.Role, out var parsedRole) ? parsedRole : ProjectRole.Member;

        var result = await _invitationService.SendInvitation(model.ProjectId, model.UserId, role, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Invitation sent successfully.";
        }

        return RedirectToAction(nameof(Details), new { id = model.ProjectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(int projectId, string userId)
    {
        var result = await _memberService.RemoveMember(projectId, userId, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Member removed from the project.";
        }

        return RedirectToAction(nameof(Details), new { id = projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMemberRole(UpdateMemberRoleViewModel model)
    {
        var result = await _memberService.UpdateMemberRole(model.ProjectId, model.UserId, model.Role, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Member role updated.";
        }

        return RedirectToAction(nameof(Details), new { id = model.ProjectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachment(int projectId, IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a file to upload.";
            return RedirectToAction(nameof(Details), new { id = projectId });
        }

        var userId = GetUserId();
        var memberCheck = await _memberService.GetMemberUserIds(projectId);
        if (!memberCheck.Contains(userId) && !IsAdmin())
        {
            TempData["ErrorMessage"] = "You must be a project member to upload attachments.";
            return RedirectToAction(nameof(Details), new { id = projectId });
        }

        using var stream = file.OpenReadStream();
        var result = await _attachmentService.UploadAsync(
            stream, file.FileName, file.ContentType, file.Length,
            AttachmentEntityType.Project, projectId, userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "File uploaded successfully.";
        }

        return RedirectToAction(nameof(Details), new { id = projectId });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int projectId, int attachmentId)
    {
        var userId = GetUserId();
        var memberCheck = await _memberService.GetMemberUserIds(projectId);
        if (!memberCheck.Contains(userId) && !IsAdmin())
        {
            TempData["ErrorMessage"] = "You must be a project member to download attachments.";
            return RedirectToAction(nameof(Details), new { id = projectId });
        }

        var result = await _attachmentService.GetByIdAsync(attachmentId);
        if (!result.Succeeded || result.Data!.EntityType != AttachmentEntityType.Project || result.Data.EntityId != projectId)
        {
            TempData["ErrorMessage"] = "Attachment not found.";
            return RedirectToAction(nameof(Details), new { id = projectId });
        }

        var attachment = result.Data;
        var stream = await _fileStorageService.GetFileStreamAsync(attachment.StoredFileName);
        return File(stream, attachment.ContentType, attachment.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(int projectId, int attachmentId)
    {
        var userId = GetUserId();

        var attachmentResult = await _attachmentService.GetByIdAsync(attachmentId);
        if (!attachmentResult.Succeeded || attachmentResult.Data!.EntityType != AttachmentEntityType.Project || attachmentResult.Data.EntityId != projectId)
        {
            TempData["ErrorMessage"] = "Attachment not found.";
            return RedirectToAction(nameof(Details), new { id = projectId });
        }

        var attachment = attachmentResult.Data;
        var dashboardResult = await _projectService.GetProjectDashboard(projectId, userId, IsAdmin());
        var currentMember = dashboardResult.Data?.Project.Members.FirstOrDefault(m => m.UserId == userId);
        var isProjectOwner = currentMember?.Role == ProjectRole.Owner;

        if (attachment.UploadedByUserId != userId && !isProjectOwner && !IsAdmin())
        {
            TempData["ErrorMessage"] = "You do not have permission to delete this attachment.";
            return RedirectToAction(nameof(Details), new { id = projectId });
        }

        var result = await _attachmentService.DeleteAsync(attachmentId, userId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Attachment deleted.";
        }

        return RedirectToAction(nameof(Details), new { id = projectId });
    }
}
