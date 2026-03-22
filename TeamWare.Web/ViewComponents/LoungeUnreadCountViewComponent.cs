using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;

namespace TeamWare.Web.ViewComponents;

public class LoungeUnreadCountViewComponent : ViewComponent
{
    private readonly ILoungeService _loungeService;

    public LoungeUnreadCountViewComponent(ILoungeService loungeService)
    {
        _loungeService = loungeService;
    }

    public async Task<IViewComponentResult> InvokeAsync(int? projectId = null)
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

        var result = await _loungeService.GetUnreadCounts(userId);
        if (!result.Succeeded)
        {
            return View(0);
        }

        var count = result.Data!
            .Where(r => r.ProjectId == projectId)
            .Select(r => r.Count)
            .FirstOrDefault();

        return View(count);
    }
}
