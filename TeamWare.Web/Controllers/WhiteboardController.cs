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
public class WhiteboardController : Controller
{
    private readonly IWhiteboardService _whiteboardService;
    private readonly IWhiteboardInvitationService _whiteboardInvitationService;
    private readonly IWhiteboardProjectService _whiteboardProjectService;
    private readonly IWhiteboardChatService _whiteboardChatService;
    private readonly IWhiteboardPresenceTracker _whiteboardPresenceTracker;
    private readonly IProjectService _projectService;
    private readonly IHubContext<WhiteboardHub> _whiteboardHubContext;
    private readonly IUserDirectoryService _userDirectoryService;
    private readonly ApplicationDbContext _dbContext;

    public WhiteboardController(
        IWhiteboardService whiteboardService,
        IWhiteboardInvitationService whiteboardInvitationService,
        IWhiteboardProjectService whiteboardProjectService,
        IWhiteboardChatService whiteboardChatService,
        IWhiteboardPresenceTracker whiteboardPresenceTracker,
        IProjectService projectService,
        IHubContext<WhiteboardHub> whiteboardHubContext,
        IUserDirectoryService userDirectoryService,
        ApplicationDbContext dbContext)
    {
        _whiteboardService = whiteboardService;
        _whiteboardInvitationService = whiteboardInvitationService;
        _whiteboardProjectService = whiteboardProjectService;
        _whiteboardChatService = whiteboardChatService;
        _whiteboardPresenceTracker = whiteboardPresenceTracker;
        _projectService = projectService;
        _whiteboardHubContext = whiteboardHubContext;
        _userDirectoryService = userDirectoryService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var result = await _whiteboardService.GetLandingPageAsync(GetUserId(), IsAdmin());
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return View(new WhiteboardLandingViewModel());
        }

        return View(new WhiteboardLandingViewModel
        {
            Whiteboards = result.Data ?? []
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateWhiteboardViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateWhiteboardViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _whiteboardService.CreateAsync(GetUserId(), model.Title);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(model);
        }

        TempData["SuccessMessage"] = "Whiteboard created successfully.";
        return RedirectToAction("Session", new { id = result.Data });
    }

    [HttpGet]
    public async Task<IActionResult> Session(int id)
    {
        var userId = GetUserId();
        var isSiteAdmin = IsAdmin();

        var accessResult = await _whiteboardService.CanAccessAsync(id, userId, isSiteAdmin);
        if (!accessResult.Succeeded)
        {
            TempData["ErrorMessage"] = accessResult.Errors.FirstOrDefault() ?? "Whiteboard not found.";
            return RedirectToAction(nameof(Index));
        }

        if (!accessResult.Data)
        {
            await _whiteboardInvitationService.CleanupInvalidInvitationsAsync(id);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var whiteboardResult = await _whiteboardService.GetByIdAsync(id);
        if (!whiteboardResult.Succeeded || whiteboardResult.Data == null)
        {
            TempData["ErrorMessage"] = whiteboardResult.Errors.FirstOrDefault() ?? "Whiteboard not found.";
            return RedirectToAction(nameof(Index));
        }

        var availableProjectsResult = await _projectService.GetProjectsForUser(userId);
        var chatMessagesResult = await _whiteboardChatService.GetMessagesAsync(id, 1, 50);

        var activeUserIds = await _whiteboardPresenceTracker.GetActiveUsersAsync(id);
        var userDisplayNames = await _dbContext.Users
            .Where(u => activeUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        whiteboardResult.Data.ActiveUsers = activeUserIds
            .Select(uid => new ActiveUserDto
            {
                UserId = uid,
                DisplayName = userDisplayNames.TryGetValue(uid, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : uid
            })
            .ToList();

        var viewModel = new WhiteboardSessionViewModel
        {
            Whiteboard = whiteboardResult.Data,
            IsOwner = whiteboardResult.Data.OwnerId == userId,
            IsPresenter = whiteboardResult.Data.CurrentPresenterId == userId,
            CanDraw = whiteboardResult.Data.CurrentPresenterId == userId,
            IsTemporary = whiteboardResult.Data.IsTemporary,
            IsSiteAdmin = isSiteAdmin,
            ProjectAssociation = BuildProjectAssociationViewModel(
                whiteboardResult.Data.Id,
                whiteboardResult.Data.ProjectId,
                whiteboardResult.Data.ProjectName,
                availableProjectsResult.Succeeded && availableProjectsResult.Data != null
                    ? availableProjectsResult.Data
                        .OrderBy(p => p.Name)
                        .Select(p => new ProjectOptionViewModel
                        {
                            Id = p.Id,
                            Name = p.Name
                        })
                        .ToList()
                    : []),
            ChatMessages = chatMessagesResult.Succeeded && chatMessagesResult.Data != null
                ? chatMessagesResult.Data
                    .OrderBy(m => m.CreatedAt)
                    .ToList()
                : [],
            AvailableProjects = availableProjectsResult.Succeeded && availableProjectsResult.Data != null
                ? availableProjectsResult.Data
                    .OrderBy(p => p.Name)
                    .Select(p => new ProjectOptionViewModel
                    {
                        Id = p.Id,
                        Name = p.Name
                    })
                    .ToList()
                : []
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _whiteboardService.DeleteAsync(id, GetUserId(), IsAdmin());
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Contains("owner", StringComparison.OrdinalIgnoreCase)
                || e.Contains("admin", StringComparison.OrdinalIgnoreCase)))
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Index));
        }

        await _whiteboardHubContext.Clients.Group(WhiteboardHub.GetGroupName(id))
            .SendAsync("BoardDeleted", id);

        TempData["SuccessMessage"] = "Whiteboard deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> InviteForm(int whiteboardId, string? query)
    {
        var viewModelResult = await BuildInviteFormViewModelAsync(whiteboardId, query);
        if (!viewModelResult.Succeeded)
        {
            if (viewModelResult.Errors.Any(e => e.Contains("owner", StringComparison.OrdinalIgnoreCase)))
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            Response.StatusCode = StatusCodes.Status404NotFound;
            return PartialView("_InviteForm", new WhiteboardInviteFormViewModel
            {
                WhiteboardId = whiteboardId,
                Query = query ?? string.Empty,
                ErrorMessage = viewModelResult.Errors.FirstOrDefault() ?? "Whiteboard not found."
            });
        }

        return PartialView("_InviteForm", viewModelResult.Data!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveToProject(int whiteboardId, int? projectId)
    {
        if (!projectId.HasValue)
        {
            return await ClearProjectInternalAsync(whiteboardId);
        }

        var result = await _whiteboardProjectService.SaveToProjectAsync(whiteboardId, projectId.Value, GetUserId());
        var viewModelResult = await BuildProjectAssociationViewModelAsync(whiteboardId);

        if (!result.Succeeded)
        {
            Response.StatusCode = result.Errors.Any(e => e.Contains("owner", StringComparison.OrdinalIgnoreCase))
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status422UnprocessableEntity;

            if (viewModelResult.Succeeded)
            {
                viewModelResult.Data!.ErrorMessage = result.Errors.FirstOrDefault() ?? "Unable to save the project association.";
                return PartialView("_ProjectAssociation", viewModelResult.Data);
            }

            return StatusCode(Response.StatusCode);
        }

        if (viewModelResult.Succeeded)
        {
            viewModelResult.Data!.StatusMessage = "Whiteboard saved to project.";
            return PartialView("_ProjectAssociation", viewModelResult.Data);
        }

        return RedirectToAction(nameof(Session), new { id = whiteboardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearProject(int whiteboardId)
    {
        return await ClearProjectInternalAsync(whiteboardId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(int whiteboardId, string[] userIds, string? query)
    {
        if (userIds.Length == 0)
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            var emptyInviteModel = await BuildInviteFormViewModelAsync(whiteboardId, query);
            if (emptyInviteModel.Succeeded)
            {
                emptyInviteModel.Data!.ErrorMessage = "Please select at least one user to invite.";
                return PartialView("_InviteForm", emptyInviteModel.Data);
            }

            return PartialView("_InviteForm", new WhiteboardInviteFormViewModel
            {
                WhiteboardId = whiteboardId,
                Query = query ?? string.Empty,
                ErrorMessage = "Please select at least one user to invite."
            });
        }

        var userId = GetUserId();
        var errors = new List<string>();
        var sentCount = 0;

        foreach (var invitedUserId in userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            var result = await _whiteboardInvitationService.InviteAsync(whiteboardId, invitedUserId, userId);
            if (result.Succeeded)
            {
                sentCount++;
                continue;
            }

            errors.AddRange(result.Errors);
        }

        var viewModelResult = await BuildInviteFormViewModelAsync(whiteboardId, query);
        if (!viewModelResult.Succeeded)
        {
            if (viewModelResult.Errors.Any(e => e.Contains("owner", StringComparison.OrdinalIgnoreCase)))
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            Response.StatusCode = StatusCodes.Status404NotFound;
            return PartialView("_InviteForm", new WhiteboardInviteFormViewModel
            {
                WhiteboardId = whiteboardId,
                Query = query ?? string.Empty,
                ErrorMessage = viewModelResult.Errors.FirstOrDefault() ?? "Whiteboard not found."
            });
        }

        if (errors.Count > 0)
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            viewModelResult.Data!.ErrorMessage = errors.First();
        }
        else
        {
            viewModelResult.Data!.StatusMessage = sentCount == 1
                ? "Invitation sent successfully."
                : $"{sentCount} invitations sent successfully.";
        }

        return PartialView("_InviteForm", viewModelResult.Data!);
    }

    private async Task<IActionResult> ClearProjectInternalAsync(int whiteboardId)
    {
        var result = await _whiteboardProjectService.ClearProjectAsync(whiteboardId, GetUserId());
        var viewModelResult = await BuildProjectAssociationViewModelAsync(whiteboardId);

        if (!result.Succeeded)
        {
            Response.StatusCode = result.Errors.Any(e => e.Contains("owner", StringComparison.OrdinalIgnoreCase))
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status422UnprocessableEntity;

            if (viewModelResult.Succeeded)
            {
                viewModelResult.Data!.ErrorMessage = result.Errors.FirstOrDefault() ?? "Unable to clear the project association.";
                return PartialView("_ProjectAssociation", viewModelResult.Data);
            }

            return StatusCode(Response.StatusCode);
        }

        if (viewModelResult.Succeeded)
        {
            viewModelResult.Data!.StatusMessage = "Whiteboard returned to temporary status.";
            return PartialView("_ProjectAssociation", viewModelResult.Data);
        }

        return RedirectToAction(nameof(Session), new { id = whiteboardId });
    }

    private async Task<ServiceResult<WhiteboardProjectAssociationViewModel>> BuildProjectAssociationViewModelAsync(int whiteboardId)
    {
        var whiteboardResult = await _whiteboardService.GetByIdAsync(whiteboardId);
        if (!whiteboardResult.Succeeded || whiteboardResult.Data == null)
        {
            return ServiceResult<WhiteboardProjectAssociationViewModel>.Failure(whiteboardResult.Errors.FirstOrDefault() ?? "Whiteboard not found.");
        }

        if (whiteboardResult.Data.OwnerId != GetUserId())
        {
            return ServiceResult<WhiteboardProjectAssociationViewModel>.Failure("Only the whiteboard owner can manage project associations.");
        }

        var availableProjectsResult = await _projectService.GetProjectsForUser(GetUserId());
        var availableProjects = availableProjectsResult.Succeeded && availableProjectsResult.Data != null
            ? availableProjectsResult.Data
                .OrderBy(p => p.Name)
                .Select(p => new ProjectOptionViewModel
                {
                    Id = p.Id,
                    Name = p.Name
                })
                .ToList()
            : [];

        return ServiceResult<WhiteboardProjectAssociationViewModel>.Success(
            BuildProjectAssociationViewModel(
                whiteboardResult.Data.Id,
                whiteboardResult.Data.ProjectId,
                whiteboardResult.Data.ProjectName,
                availableProjects));
    }

    private static WhiteboardProjectAssociationViewModel BuildProjectAssociationViewModel(
        int whiteboardId,
        int? projectId,
        string? projectName,
        List<ProjectOptionViewModel> availableProjects)
    {
        return new WhiteboardProjectAssociationViewModel
        {
            WhiteboardId = whiteboardId,
            ProjectId = projectId,
            ProjectName = projectName,
            AvailableProjects = availableProjects
        };
    }

    private async Task<ServiceResult<WhiteboardInviteFormViewModel>> BuildInviteFormViewModelAsync(int whiteboardId, string? query)
    {
        var whiteboardResult = await _whiteboardService.GetByIdAsync(whiteboardId);
        if (!whiteboardResult.Succeeded || whiteboardResult.Data == null)
        {
            return ServiceResult<WhiteboardInviteFormViewModel>.Failure(whiteboardResult.Errors.FirstOrDefault() ?? "Whiteboard not found.");
        }

        if (whiteboardResult.Data.OwnerId != GetUserId())
        {
            return ServiceResult<WhiteboardInviteFormViewModel>.Failure("Only the whiteboard owner can invite users.");
        }

        var invitedUserIds = new HashSet<string>(whiteboardResult.Data.Invitations.Select(i => i.UserId));
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var candidates = new List<WhiteboardInviteCandidateViewModel>();

        if (!string.IsNullOrWhiteSpace(normalizedQuery) && normalizedQuery.Length >= 2)
        {
            var searchResult = await _userDirectoryService.SearchUsers(normalizedQuery, 1, 10);
            if (searchResult.Succeeded && searchResult.Data != null)
            {
                candidates = searchResult.Data.Items
                    .Where(u => u.UserId != whiteboardResult.Data.OwnerId)
                    .Select(u => new WhiteboardInviteCandidateViewModel
                    {
                        UserId = u.UserId,
                        DisplayName = u.DisplayName,
                        Email = u.Email,
                        IsAlreadyInvited = invitedUserIds.Contains(u.UserId)
                    })
                    .ToList();
            }
        }

        return ServiceResult<WhiteboardInviteFormViewModel>.Success(new WhiteboardInviteFormViewModel
        {
            WhiteboardId = whiteboardId,
            Query = normalizedQuery,
            Candidates = candidates,
            InvitedUsers = whiteboardResult.Data.Invitations
                .OrderBy(i => i.UserDisplayName)
                .ToList()
        });
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private bool IsAdmin() => User.IsInRole("Admin");
}
