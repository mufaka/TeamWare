using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
public class LoungeController : Controller
{
    private readonly ILoungeService _loungeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHubContext<LoungeHub> _hubContext;

    public LoungeController(
        ILoungeService loungeService,
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IHubContext<LoungeHub> hubContext)
    {
        _loungeService = loungeService;
        _dbContext = dbContext;
        _userManager = userManager;
        _hubContext = hubContext;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> Room(int? projectId)
    {
        var userId = GetUserId();

        // Authorize: project members for project rooms, any authenticated user for #general
        if (projectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                return Forbid();
            }
        }

        var roomName = "#general";
        if (projectId.HasValue)
        {
            var project = await _dbContext.Projects.FindAsync(projectId.Value);
            if (project == null)
            {
                return NotFound();
            }
            roomName = project.Name;
        }

        // Load initial messages
        var messagesResult = await _loungeService.GetMessages(projectId, null, 50);
        var pinnedResult = await _loungeService.GetPinnedMessages(projectId);
        var readPositionResult = await _loungeService.GetReadPosition(userId, projectId);

        // Determine user's role in project for authorization
        var isProjectAdminOrOwner = false;
        if (projectId.HasValue)
        {
            var member = await _dbContext.ProjectMembers
                .FirstOrDefaultAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            isProjectAdminOrOwner = member != null && (member.Role == ProjectRole.Admin || member.Role == ProjectRole.Owner);
        }
        var isSiteAdmin = IsAdmin();

        // Load members for mention autocomplete
        var members = await GetRoomMembers(projectId);

        var viewModel = new LoungeRoomViewModel
        {
            ProjectId = projectId,
            RoomName = roomName,
            Messages = MapMessages(messagesResult.Data ?? new List<LoungeMessage>(), userId, isProjectAdminOrOwner, isSiteAdmin, projectId.HasValue),
            PinnedMessages = MapMessages(pinnedResult.Data ?? new List<LoungeMessage>(), userId, isProjectAdminOrOwner, isSiteAdmin, projectId.HasValue),
            LastReadMessageId = readPositionResult.Data,
            CanCreateTask = projectId.HasValue,
            Members = members
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Messages(int? projectId, DateTime? before, int count = 50)
    {
        var userId = GetUserId();

        // SEC-04: Clamp count to a reasonable range
        count = Math.Clamp(count, 1, 100);

        // Authorize
        if (projectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                return Forbid();
            }
        }

        var result = await _loungeService.GetMessages(projectId, before, count);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var isProjectAdminOrOwner = false;
        if (projectId.HasValue)
        {
            var member = await _dbContext.ProjectMembers
                .FirstOrDefaultAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            isProjectAdminOrOwner = member != null && (member.Role == ProjectRole.Admin || member.Role == ProjectRole.Owner);
        }
        var isSiteAdmin = IsAdmin();

        var viewModels = MapMessages(result.Data!, userId, isProjectAdminOrOwner, isSiteAdmin, projectId.HasValue);

        return PartialView("_MessageList", viewModels);
    }

    [HttpGet]
    public async Task<IActionResult> PinnedMessages(int? projectId)
    {
        var userId = GetUserId();

        // Authorize
        if (projectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                return Forbid();
            }
        }

        var result = await _loungeService.GetPinnedMessages(projectId);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var isProjectAdminOrOwner = false;
        if (projectId.HasValue)
        {
            var member = await _dbContext.ProjectMembers
                .FirstOrDefaultAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            isProjectAdminOrOwner = member != null && (member.Role == ProjectRole.Admin || member.Role == ProjectRole.Owner);
        }
        var isSiteAdmin = IsAdmin();

        var viewModels = MapMessages(result.Data!, userId, isProjectAdminOrOwner, isSiteAdmin, projectId.HasValue);

        return PartialView("_PinnedMessages", viewModels);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTaskFromMessage(int messageId)
    {
        var userId = GetUserId();

        var result = await _loungeService.CreateTaskFromMessage(messageId, userId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();

            if (Request.Headers["HX-Request"] == "true")
            {
                Response.StatusCode = 422;
                return Content(result.Errors.FirstOrDefault() ?? "Failed to create task.");
            }
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var task = result.Data!;

        // Get the message to find the room for broadcasting
        var messageResult = await _loungeService.GetMessage(messageId);
        if (messageResult.Succeeded)
        {
            var groupName = LoungeHub.GetRoomGroupName(messageResult.Data!.ProjectId);
            await _hubContext.Clients.Group(groupName).SendAsync("TaskCreatedFromMessage", new
            {
                MessageId = messageId,
                TaskId = task.Id,
                TaskTitle = task.Title
            });
        }

        if (Request.Headers["HX-Request"] == "true")
        {
            return Content($"<span class=\"text-xs text-green-600 dark:text-green-400\">Task #{task.Id} created</span>", "text/html");
        }

        return RedirectToAction("Details", "Task", new { id = task.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PinMessage(int messageId)
    {
        var userId = GetUserId();
        var isSiteAdmin = IsAdmin();

        var result = await _loungeService.PinMessage(messageId, userId, isSiteAdmin);
        if (!result.Succeeded)
        {
            if (Request.Headers["HX-Request"] == "true")
            {
                Response.StatusCode = 422;
                return Content(result.Errors.FirstOrDefault() ?? "Failed to pin message.");
            }
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var message = result.Data!;
        var groupName = LoungeHub.GetRoomGroupName(message.ProjectId);
        await _hubContext.Clients.Group(groupName).SendAsync("MessagePinned", new
        {
            message.Id,
            message.IsPinned,
            message.PinnedAt,
            PinnedByDisplayName = message.PinnedByUser?.DisplayName
        });

        if (Request.Headers["HX-Request"] == "true")
        {
            return await PinnedMessages(message.ProjectId);
        }

        return RedirectToAction("Room", new { projectId = message.ProjectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnpinMessage(int messageId)
    {
        var userId = GetUserId();
        var isSiteAdmin = IsAdmin();

        var result = await _loungeService.UnpinMessage(messageId, userId, isSiteAdmin);
        if (!result.Succeeded)
        {
            if (Request.Headers["HX-Request"] == "true")
            {
                Response.StatusCode = 422;
                return Content(result.Errors.FirstOrDefault() ?? "Failed to unpin message.");
            }
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var message = result.Data!;
        var groupName = LoungeHub.GetRoomGroupName(message.ProjectId);
        await _hubContext.Clients.Group(groupName).SendAsync("MessageUnpinned", new
        {
            message.Id
        });

        if (Request.Headers["HX-Request"] == "true")
        {
            return await PinnedMessages(message.ProjectId);
        }

        return RedirectToAction("Room", new { projectId = message.ProjectId });
    }

    [HttpGet]
    public async Task<IActionResult> MemberSearch(int? projectId, string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length > 100)
        {
            return Json(Array.Empty<object>());
        }

        var userId = GetUserId();

        // Authorize: must have room access to search members
        if (projectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                return Forbid();
            }
        }

        var members = await GetRoomMembers(projectId);
        var filtered = members
            .Where(m => m.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || m.UserName.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .Select(m => new { m.UserId, m.DisplayName, m.UserName })
            .ToList();

        return Json(filtered);
    }

    private List<LoungeMessageViewModel> MapMessages(
        List<LoungeMessage> messages,
        string currentUserId,
        bool isProjectAdminOrOwner,
        bool isSiteAdmin,
        bool isProjectRoom)
    {
        return messages.Select(m => new LoungeMessageViewModel
        {
            Id = m.Id,
            ProjectId = m.ProjectId,
            AuthorId = m.UserId,
            AuthorDisplayName = m.User?.DisplayName ?? "Unknown",
            AuthorAvatarUrl = m.User?.AvatarUrl,
            Content = m.Content,
            CreatedAt = m.CreatedAt,
            IsEdited = m.IsEdited,
            EditedAt = m.EditedAt,
            IsPinned = m.IsPinned,
            PinnedByDisplayName = m.PinnedByUser?.DisplayName,
            PinnedAt = m.PinnedAt,
            CreatedTaskId = m.CreatedTaskId,
            Reactions = m.Reactions?.GroupBy(r => r.ReactionType)
                .Select(g => new ReactionSummary
                {
                    ReactionType = g.Key,
                    Count = g.Count(),
                    CurrentUserReacted = g.Any(r => r.UserId == currentUserId)
                }).ToList() ?? new List<ReactionSummary>(),
            CanEdit = m.UserId == currentUserId,
            CanDelete = m.UserId == currentUserId || isProjectAdminOrOwner || isSiteAdmin,
            CanPin = isProjectAdminOrOwner || isSiteAdmin,
            CanCreateTask = isProjectRoom && m.CreatedTaskId == null
        }).ToList();
    }

    private async Task<List<LoungeMemberViewModel>> GetRoomMembers(int? projectId)
    {
        if (projectId.HasValue)
        {
            return await _dbContext.ProjectMembers
                .Where(pm => pm.ProjectId == projectId.Value)
                .Select(pm => new LoungeMemberViewModel
                {
                    UserId = pm.UserId,
                    DisplayName = pm.User.DisplayName,
                    UserName = pm.User.UserName ?? string.Empty
                })
                .ToListAsync();
        }

        // #general — all users
        return await _dbContext.Users
            .Select(u => new LoungeMemberViewModel
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                UserName = u.UserName ?? string.Empty
            })
            .ToListAsync();
    }
}
