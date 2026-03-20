using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;

namespace TeamWare.Web.ViewComponents;

public class ReviewStatusViewComponent : ViewComponent
{
    private readonly IReviewService _reviewService;

    public ReviewStatusViewComponent(IReviewService reviewService)
    {
        _reviewService = reviewService;
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

        var lastReviewDate = await _reviewService.GetLastReviewDate(userId);
        var isReviewDue = await _reviewService.IsReviewDue(userId);

        return View(new ReviewStatusModel
        {
            LastReviewDate = lastReviewDate,
            IsReviewDue = isReviewDue
        });
    }
}
