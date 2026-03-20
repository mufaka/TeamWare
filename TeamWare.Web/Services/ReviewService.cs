using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class ReviewService : IReviewService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;

    public ReviewService(ApplicationDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<ServiceResult<ReviewData>> StartReview(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<ReviewData>.Failure("User ID is required.");
        }

        var unprocessedInboxItems = await _context.InboxItems
            .Where(i => i.UserId == userId && i.Status == InboxItemStatus.Unprocessed)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync();

        var assignedTaskIds = await _context.TaskAssignments
            .Where(ta => ta.UserId == userId)
            .Select(ta => ta.TaskItemId)
            .ToListAsync();

        var activeTasks = await _context.TaskItems
            .Where(t => assignedTaskIds.Contains(t.Id)
                && t.Status != TaskItemStatus.Done
                && !t.IsNextAction
                && !t.IsSomedayMaybe)
            .Include(t => t.Project)
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ToListAsync();

        var nextActions = await _context.TaskItems
            .Where(t => assignedTaskIds.Contains(t.Id)
                && t.IsNextAction
                && t.Status != TaskItemStatus.Done)
            .Include(t => t.Project)
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ToListAsync();

        var somedayMaybeItems = await _context.TaskItems
            .Where(t => assignedTaskIds.Contains(t.Id)
                && t.IsSomedayMaybe
                && t.Status != TaskItemStatus.Done)
            .Include(t => t.Project)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        var lastReviewDate = await GetLastReviewDate(userId);

        var reviewData = new ReviewData
        {
            UnprocessedInboxItems = unprocessedInboxItems,
            ActiveTasks = activeTasks,
            NextActions = nextActions,
            SomedayMaybeItems = somedayMaybeItems,
            LastReviewDate = lastReviewDate
        };

        return ServiceResult<ReviewData>.Success(reviewData);
    }

    public async Task<ServiceResult> CompleteReview(string userId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult.Failure("User ID is required.");
        }

        if (notes != null && notes.Length > 2000)
        {
            return ServiceResult.Failure("Notes must not exceed 2000 characters.");
        }

        var review = new UserReview
        {
            UserId = userId,
            CompletedAt = DateTime.UtcNow,
            Notes = notes
        };

        _context.UserReviews.Add(review);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<DateTime?> GetLastReviewDate(string userId)
    {
        var lastReview = await _context.UserReviews
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync();

        return lastReview?.CompletedAt;
    }

    public async Task<bool> IsReviewDue(string userId, int intervalDays = 7)
    {
        var lastReviewDate = await GetLastReviewDate(userId);

        if (lastReviewDate == null)
        {
            return true;
        }

        return DateTime.UtcNow - lastReviewDate.Value >= TimeSpan.FromDays(intervalDays);
    }
}
