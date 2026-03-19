using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;

namespace TeamWare.Web.ViewComponents;

public class InboxCountViewComponent : ViewComponent
{
    private readonly IInboxService _inboxService;

    public InboxCountViewComponent(IInboxService inboxService)
    {
        _inboxService = inboxService;
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

        var result = await _inboxService.GetUnprocessedCount(userId);
        var count = result.Succeeded ? result.Data : 0;

        return View(count);
    }
}
