using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;

namespace TeamWare.Web.Controllers;

[Authorize]
public class AiController : Controller
{
    private readonly IAiAssistantService _aiAssistantService;

    public AiController(IAiAssistantService aiAssistantService)
    {
        _aiAssistantService = aiAssistantService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> IsAvailable()
    {
        var available = await _aiAssistantService.IsAvailable();
        return Json(new { available });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RewriteProjectDescription()
    {
        // Implemented in Phase 23
        return Json(new { success = false, error = "Not implemented." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RewriteTaskDescription()
    {
        // Implemented in Phase 23
        return Json(new { success = false, error = "Not implemented." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PolishComment()
    {
        // Implemented in Phase 23
        return Json(new { success = false, error = "Not implemented." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExpandInboxItem()
    {
        // Implemented in Phase 23
        return Json(new { success = false, error = "Not implemented." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ProjectSummary()
    {
        // Implemented in Phase 24
        return Json(new { success = false, error = "Not implemented." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PersonalDigest()
    {
        // Implemented in Phase 24
        return Json(new { success = false, error = "Not implemented." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ReviewPreparation()
    {
        // Implemented in Phase 24
        return Json(new { success = false, error = "Not implemented." });
    }
}
