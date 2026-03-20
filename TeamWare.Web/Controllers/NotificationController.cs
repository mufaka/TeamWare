using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class NotificationController : Controller
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
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
