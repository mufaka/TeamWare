using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class InvitationController : Controller
{
    private readonly IProjectInvitationService _invitationService;

    public InvitationController(IProjectInvitationService invitationService)
    {
        _invitationService = invitationService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(SendInvitationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please select a valid user.";
            return RedirectToAction("Details", "Project", new { id = model.ProjectId });
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

        return RedirectToAction("Details", "Project", new { id = model.ProjectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        var result = await _invitationService.AcceptInvitation(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Invitation accepted. You are now a project member.";
            return RedirectToAction("Details", "Project", new { id = result.Data!.ProjectId });
        }

        return RedirectToAction(nameof(PendingForUser));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(int id)
    {
        var result = await _invitationService.DeclineInvitation(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Invitation declined.";
        }

        return RedirectToAction(nameof(PendingForUser));
    }

    [HttpGet]
    public async Task<IActionResult> PendingForUser()
    {
        var result = await _invitationService.GetPendingInvitationsForUser(GetUserId());

        var viewModel = new PendingInvitationsViewModel
        {
            Invitations = result.Succeeded && result.Data != null
                ? result.Data.Select(MapToViewModel).ToList()
                : new List<ProjectInvitationViewModel>()
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> PendingForProject(int projectId)
    {
        var result = await _invitationService.GetPendingInvitationsForProject(projectId, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction("Details", "Project", new { id = projectId });
        }

        var viewModel = new PendingInvitationsViewModel
        {
            Invitations = result.Data!.Select(MapToViewModel).ToList()
        };

        ViewData["ProjectId"] = projectId;
        return View(viewModel);
    }

    private static ProjectInvitationViewModel MapToViewModel(ProjectInvitation invitation)
    {
        return new ProjectInvitationViewModel
        {
            Id = invitation.Id,
            ProjectId = invitation.ProjectId,
            ProjectName = invitation.Project?.Name ?? string.Empty,
            InvitedUserId = invitation.InvitedUserId,
            InvitedUserDisplayName = invitation.InvitedUser?.DisplayName ?? string.Empty,
            InvitedUserEmail = invitation.InvitedUser?.Email ?? string.Empty,
            InvitedByUserDisplayName = invitation.InvitedByUser?.DisplayName ?? string.Empty,
            Role = invitation.Role,
            Status = invitation.Status,
            CreatedAt = invitation.CreatedAt,
            RespondedAt = invitation.RespondedAt
        };
    }
}
