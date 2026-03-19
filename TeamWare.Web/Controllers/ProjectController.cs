using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class ProjectController : Controller
{
    private readonly IProjectService _projectService;
    private readonly IProjectMemberService _memberService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProjectController(
        IProjectService projectService,
        IProjectMemberService memberService,
        UserManager<ApplicationUser> userManager)
    {
        _projectService = projectService;
        _memberService = memberService;
        _userManager = userManager;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

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
        var result = await _projectService.GetProjectDashboard(id, GetUserId());

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

        var result = await _projectService.UpdateProject(model.Id, model.Name, model.Description, GetUserId());

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
        var result = await _projectService.GetProjectDashboard(id, userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Index));
        }

        var dashboard = result.Data!;
        var currentUserMember = dashboard.Project.Members.FirstOrDefault(m => m.UserId == userId);

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
            Members = dashboard.Project.Members.Select(m => new ProjectMemberViewModel
            {
                UserId = m.UserId,
                DisplayName = m.User.DisplayName,
                Email = m.User.Email ?? string.Empty,
                Role = m.Role,
                JoinedAt = m.JoinedAt
            }).OrderByDescending(m => m.Role).ThenBy(m => m.DisplayName).ToList()
        };

        return View(viewModel);
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteMember(InviteMemberViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please provide a valid email address.";
            return RedirectToAction(nameof(Details), new { id = model.ProjectId });
        }

        var targetUser = await _userManager.FindByEmailAsync(model.Email);
        if (targetUser == null)
        {
            TempData["ErrorMessage"] = "No user found with that email address.";
            return RedirectToAction(nameof(Details), new { id = model.ProjectId });
        }

        var result = await _memberService.InviteMember(model.ProjectId, targetUser.Id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = $"{targetUser.DisplayName} has been added to the project.";
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
}
