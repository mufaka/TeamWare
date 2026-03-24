using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Services;

namespace TeamWare.Web.Controllers;

[Authorize]
public class AiController : Controller
{
    private const int MaxInputLength = 4000;

    private readonly IAiAssistantService _aiAssistantService;
    private readonly ApplicationDbContext _context;

    public AiController(IAiAssistantService aiAssistantService, ApplicationDbContext context)
    {
        _aiAssistantService = aiAssistantService;
        _context = context;
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
    public async Task<IActionResult> RewriteProjectDescription(int projectId, string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Json(new { success = false, error = "Description is required." });
        }

        if (description.Length > MaxInputLength)
        {
            return Json(new { success = false, error = $"Description must be {MaxInputLength} characters or fewer." });
        }

        if (!await IsProjectMember(projectId, GetUserId()))
        {
            return Json(new { success = false, error = "Access denied." });
        }

        var result = await _aiAssistantService.RewriteProjectDescription(description);

        if (!result.Succeeded)
        {
            return Json(new { success = false, error = result.Errors.FirstOrDefault() });
        }

        return Json(new { success = true, suggestion = result.Data });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RewriteTaskDescription(int taskId, string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Json(new { success = false, error = "Description is required." });
        }

        if (description.Length > MaxInputLength)
        {
            return Json(new { success = false, error = $"Description must be {MaxInputLength} characters or fewer." });
        }

        var task = await _context.TaskItems.FindAsync(taskId);

        if (task == null)
        {
            return Json(new { success = false, error = "Task not found." });
        }

        if (!await IsProjectMember(task.ProjectId, GetUserId()))
        {
            return Json(new { success = false, error = "Access denied." });
        }

        var result = await _aiAssistantService.RewriteTaskDescription(description);

        if (!result.Succeeded)
        {
            return Json(new { success = false, error = result.Errors.FirstOrDefault() });
        }

        return Json(new { success = true, suggestion = result.Data });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PolishComment(int taskId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return Json(new { success = false, error = "Comment is required." });
        }

        if (comment.Length > MaxInputLength)
        {
            return Json(new { success = false, error = $"Comment must be {MaxInputLength} characters or fewer." });
        }

        var task = await _context.TaskItems.FindAsync(taskId);

        if (task == null)
        {
            return Json(new { success = false, error = "Task not found." });
        }

        if (!await IsProjectMember(task.ProjectId, GetUserId()))
        {
            return Json(new { success = false, error = "Access denied." });
        }

        var result = await _aiAssistantService.PolishComment(comment);

        if (!result.Succeeded)
        {
            return Json(new { success = false, error = result.Errors.FirstOrDefault() });
        }

        return Json(new { success = true, suggestion = result.Data });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExpandInboxItem(int inboxItemId, string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Json(new { success = false, error = "Title is required." });
        }

        var inboxItem = await _context.InboxItems.FindAsync(inboxItemId);

        if (inboxItem == null || inboxItem.UserId != GetUserId())
        {
            return Json(new { success = false, error = "Access denied." });
        }

        var result = await _aiAssistantService.ExpandInboxItem(title, description);

        if (!result.Succeeded)
        {
            return Json(new { success = false, error = result.Errors.FirstOrDefault() });
        }

        return Json(new { success = true, suggestion = result.Data });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProjectSummary(int projectId, string period)
    {
        if (!Enum.TryParse<SummaryPeriod>(period, ignoreCase: true, out var summaryPeriod))
        {
            return Json(new { success = false, error = "Invalid period. Use Today, ThisWeek, or ThisMonth." });
        }

        var userId = GetUserId();

        if (!await IsProjectMember(projectId, userId))
        {
            return Json(new { success = false, error = "Access denied." });
        }

        var result = await _aiAssistantService.GenerateProjectSummary(projectId, userId, summaryPeriod);

        if (!result.Succeeded)
        {
            return Json(new { success = false, error = result.Errors.FirstOrDefault() });
        }

        return Json(new { success = true, summary = result.Data });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PersonalDigest()
    {
        var result = await _aiAssistantService.GeneratePersonalDigest(GetUserId());

        if (!result.Succeeded)
        {
            return Json(new { success = false, error = result.Errors.FirstOrDefault() });
        }

        return Json(new { success = true, summary = result.Data });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewPreparation()
    {
        var result = await _aiAssistantService.GenerateReviewPreparation(GetUserId());

        if (!result.Succeeded)
        {
            return Json(new { success = false, error = result.Errors.FirstOrDefault() });
        }

        return Json(new { success = true, summary = result.Data });
    }

    private async Task<bool> IsProjectMember(int projectId, string userId)
    {
        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
    }
}
