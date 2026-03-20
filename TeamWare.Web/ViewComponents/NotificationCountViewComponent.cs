using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;

namespace TeamWare.Web.ViewComponents;

public class NotificationCountViewComponent : ViewComponent
{
    private readonly INotificationService _notificationService;

    public NotificationCountViewComponent(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal?.Identity?.IsAuthenticated != true)
        {
            return Content(string.Empty);
        }

        var userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Content(string.Empty);
        }

        var count = await _notificationService.GetUnreadCount(userId);

        return View(count);
    }
}
