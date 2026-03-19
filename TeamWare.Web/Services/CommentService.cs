using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class CommentService : ICommentService
{
    private readonly ApplicationDbContext _context;

    public CommentService(ApplicationDbContext context)
    {
        _context = context;
    }

    private async Task<bool> IsProjectMember(int projectId, string userId)
    {
        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
    }

    public async Task<ServiceResult<Comment>> AddComment(int taskItemId, string content, string authorId)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ServiceResult<Comment>.Failure("Comment content is required.");
        }

        var task = await _context.TaskItems.FindAsync(taskItemId);
        if (task == null)
        {
            return ServiceResult<Comment>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, authorId))
        {
            return ServiceResult<Comment>.Failure("You must be a project member to add comments.");
        }

        var comment = new Comment
        {
            TaskItemId = taskItemId,
            AuthorId = authorId,
            Content = content.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        // Reload with Author for display
        await _context.Entry(comment).Reference(c => c.Author).LoadAsync();

        return ServiceResult<Comment>.Success(comment);
    }

    public async Task<ServiceResult<Comment>> EditComment(int commentId, string newContent, string userId)
    {
        if (string.IsNullOrWhiteSpace(newContent))
        {
            return ServiceResult<Comment>.Failure("Comment content is required.");
        }

        var comment = await _context.Comments.FindAsync(commentId);
        if (comment == null)
        {
            return ServiceResult<Comment>.Failure("Comment not found.");
        }

        if (comment.AuthorId != userId)
        {
            return ServiceResult<Comment>.Failure("You can only edit your own comments.");
        }

        comment.Content = newContent.Trim();
        comment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _context.Entry(comment).Reference(c => c.Author).LoadAsync();

        return ServiceResult<Comment>.Success(comment);
    }

    public async Task<ServiceResult> DeleteComment(int commentId, string userId)
    {
        var comment = await _context.Comments.FindAsync(commentId);
        if (comment == null)
        {
            return ServiceResult.Failure("Comment not found.");
        }

        if (comment.AuthorId != userId)
        {
            return ServiceResult.Failure("You can only delete your own comments.");
        }

        _context.Comments.Remove(comment);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<Comment>>> GetCommentsForTask(int taskItemId, string userId)
    {
        var task = await _context.TaskItems.FindAsync(taskItemId);
        if (task == null)
        {
            return ServiceResult<List<Comment>>.Failure("Task not found.");
        }

        if (!await IsProjectMember(task.ProjectId, userId))
        {
            return ServiceResult<List<Comment>>.Failure("You must be a project member to view comments.");
        }

        var comments = await _context.Comments
            .Where(c => c.TaskItemId == taskItemId)
            .Include(c => c.Author)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        return ServiceResult<List<Comment>>.Success(comments);
    }
}
