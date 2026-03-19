using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface ICommentService
{
    Task<ServiceResult<Comment>> AddComment(int taskItemId, string content, string authorId);

    Task<ServiceResult<Comment>> EditComment(int commentId, string newContent, string userId);

    Task<ServiceResult> DeleteComment(int commentId, string userId);

    Task<ServiceResult<List<Comment>>> GetCommentsForTask(int taskItemId, string userId);
}
