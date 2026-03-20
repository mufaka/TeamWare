using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IReviewService
{
    Task<ServiceResult<ReviewData>> StartReview(string userId);

    Task<ServiceResult> CompleteReview(string userId, string? notes = null);

    Task<DateTime?> GetLastReviewDate(string userId);

    Task<bool> IsReviewDue(string userId, int intervalDays = 7);
}
