using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class CommentController : Controller
{
    private readonly ICommentService _commentService;

    public CommentController(ICommentService commentService)
    {
        _commentService = commentService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(AddCommentViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Comment content is required.";
            return RedirectToAction("Details", "Task", new { id = model.TaskItemId });
        }

        var result = await _commentService.AddComment(model.TaskItemId, model.Content, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Comment added.";
        }

        if (Request.Headers["HX-Request"] == "true")
        {
            var commentsResult = await _commentService.GetCommentsForTask(model.TaskItemId, GetUserId());
            if (commentsResult.Succeeded)
            {
                var userId = GetUserId();
                var viewModels = commentsResult.Data!.Select(c => new CommentViewModel
                {
                    Id = c.Id,
                    TaskItemId = c.TaskItemId,
                    AuthorId = c.AuthorId,
                    AuthorDisplayName = c.Author.DisplayName,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    CanEditOrDelete = c.AuthorId == userId
                }).ToList();

                return PartialView("_CommentList", viewModels);
            }
        }

        return RedirectToAction("Details", "Task", new { id = model.TaskItemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditCommentViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Comment content is required.";
            return RedirectToAction("Details", "Task", new { id = model.TaskItemId });
        }

        var result = await _commentService.EditComment(model.CommentId, model.Content, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Comment updated.";
        }

        if (Request.Headers["HX-Request"] == "true")
        {
            var commentsResult = await _commentService.GetCommentsForTask(model.TaskItemId, GetUserId());
            if (commentsResult.Succeeded)
            {
                var userId = GetUserId();
                var viewModels = commentsResult.Data!.Select(c => new CommentViewModel
                {
                    Id = c.Id,
                    TaskItemId = c.TaskItemId,
                    AuthorId = c.AuthorId,
                    AuthorDisplayName = c.Author.DisplayName,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    CanEditOrDelete = c.AuthorId == userId
                }).ToList();

                return PartialView("_CommentList", viewModels);
            }
        }

        return RedirectToAction("Details", "Task", new { id = model.TaskItemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int commentId, int taskItemId)
    {
        var result = await _commentService.DeleteComment(commentId, GetUserId());

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Comment deleted.";
        }

        if (Request.Headers["HX-Request"] == "true")
        {
            var commentsResult = await _commentService.GetCommentsForTask(taskItemId, GetUserId());
            if (commentsResult.Succeeded)
            {
                var userId = GetUserId();
                var viewModels = commentsResult.Data!.Select(c => new CommentViewModel
                {
                    Id = c.Id,
                    TaskItemId = c.TaskItemId,
                    AuthorId = c.AuthorId,
                    AuthorDisplayName = c.Author.DisplayName,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    CanEditOrDelete = c.AuthorId == userId
                }).ToList();

                return PartialView("_CommentList", viewModels);
            }
        }

        return RedirectToAction("Details", "Task", new { id = taskItemId });
    }
}
