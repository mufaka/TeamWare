using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TeamWare.Web.Services;

namespace TeamWare.Web.Controllers;

[Authorize]
public class ActivityController : Controller
{
    private readonly IGlobalActivityService _globalActivityService;

    public ActivityController(IGlobalActivityService globalActivityService)
    {
        _globalActivityService = globalActivityService;
    }

    public async Task<IActionResult> GlobalFeed(int count = 20)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _globalActivityService.GetGlobalActivityFeed(userId, count);

        if (!result.Succeeded)
        {
            return PartialView("_GlobalActivityFeed", new List<ViewModels.GlobalActivityFeedEntryViewModel>());
        }

        return PartialView("_GlobalActivityFeed", result.Data);
    }
}
