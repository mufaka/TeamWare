using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class ReviewController : Controller
{
    private readonly IReviewService _reviewService;

    public ReviewController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(int step = 1)
    {
        if (step < 1 || step > 3)
        {
            step = 1;
        }

        var userId = GetUserId();
        var result = await _reviewService.StartReview(userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction("Index", "Home");
        }

        var data = result.Data!;

        var viewModel = new ReviewViewModel
        {
            UnprocessedInboxItems = data.UnprocessedInboxItems.Select(i => new InboxItemViewModel
            {
                Id = i.Id,
                Title = i.Title,
                Description = i.Description,
                Status = i.Status,
                CreatedAt = i.CreatedAt
            }).ToList(),
            ActiveTasks = data.ActiveTasks.Select(MapTask).ToList(),
            NextActions = data.NextActions.Select(MapTask).ToList(),
            SomedayMaybeItems = data.SomedayMaybeItems.Select(MapTask).ToList(),
            LastReviewDate = data.LastReviewDate,
            CurrentStep = step
        };

        if (Request.Headers["HX-Request"] == "true")
        {
            return PartialView($"_ReviewStep{step}", viewModel);
        }

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(CompleteReviewViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Invalid review data.";
            return RedirectToAction(nameof(Index), new { step = 3 });
        }

        var result = await _reviewService.CompleteReview(GetUserId(), model.Notes);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Index), new { step = 3 });
        }

        TempData["SuccessMessage"] = "Review completed successfully!";
        return RedirectToAction(nameof(Index));
    }

    private static ReviewTaskViewModel MapTask(Web.Models.TaskItem task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Status = task.Status,
        Priority = task.Priority,
        DueDate = task.DueDate,
        IsNextAction = task.IsNextAction,
        IsSomedayMaybe = task.IsSomedayMaybe,
        ProjectName = task.Project?.Name ?? string.Empty,
        ProjectId = task.ProjectId
    };
}
