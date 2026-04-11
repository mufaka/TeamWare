using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class NotificationController : Controller
{
    private readonly INotificationService _notificationService;
    private readonly IWhiteboardService _whiteboardService;
    private readonly IWhiteboardInvitationService _whiteboardInvitationService;

    public NotificationController(
        INotificationService notificationService,
        IWhiteboardService whiteboardService,
        IWhiteboardInvitationService whiteboardInvitationService)
    {
        _notificationService = notificationService;
        _whiteboardService = whiteboardService;
        _whiteboardInvitationService = whiteboardInvitationService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var notifications = await _notificationService.GetUnreadForUser(userId);
        var unreadCount = await _notificationService.GetUnreadCount(userId);

        var viewModel = new NotificationListViewModel
        {
            Notifications = notifications.Select(n => new NotificationViewModel
            {
                Id = n.Id,
                Message = n.Message,
                Type = n.Type,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
                ReferenceId = n.ReferenceId
            }).ToList(),
            UnreadCount = unreadCount
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var result = await _notificationService.MarkAsRead(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }

        if (Request.Headers["HX-Request"] == "true")
        {
            return await NotificationDropdownPartial();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(int id)
    {
        var result = await _notificationService.DismissNotification(id, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }

        if (Request.Headers["HX-Request"] == "true")
        {
            return await NotificationDropdownPartial();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> DropdownContent()
    {
        return await NotificationDropdownPartial();
    }

    [HttpGet]
    public async Task<IActionResult> Follow(int id)
    {
        var userId = GetUserId();
        var notifications = await _notificationService.GetUnreadForUser(userId);
        var notification = notifications.FirstOrDefault(n => n.Id == id);

        if (notification == null)
        {
            TempData["ErrorMessage"] = "Notification not found.";
            return RedirectToAction(nameof(Index));
        }

        await _notificationService.MarkAsRead(notification.Id, userId);

        if (notification.Type == NotificationType.WhiteboardInvitation && notification.ReferenceId.HasValue)
        {
            var whiteboardId = notification.ReferenceId.Value;
            var accessResult = await _whiteboardService.CanAccessAsync(whiteboardId, userId, User.IsInRole("Admin"));

            if (accessResult.Succeeded && accessResult.Data)
            {
                return RedirectToAction("Session", "Whiteboard", new { id = whiteboardId });
            }

            var cleanupResult = await _whiteboardInvitationService.CleanupInvalidInvitationsAsync(whiteboardId);
            if (!cleanupResult.Succeeded)
            {
                await _whiteboardInvitationService.RevokeAsync(whiteboardId, userId);
                TempData["ErrorMessage"] = "This whiteboard is no longer available.";
            }
            else
            {
                TempData["ErrorMessage"] = "You no longer have permission to access this whiteboard.";
            }

            return RedirectToAction("Index", "Whiteboard");
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> NotificationDropdownPartial()
    {
        var userId = GetUserId();
        var notifications = await _notificationService.GetUnreadForUser(userId);

        var viewModels = notifications.Select(n => new NotificationViewModel
        {
            Id = n.Id,
            Message = n.Message,
            Type = n.Type,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt,
            ReferenceId = n.ReferenceId
        }).ToList();

        return PartialView("_NotificationDropdown", viewModels);
    }
}
