using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class CommentController : Controller
{
    private readonly ICommentService _commentService;
    private readonly IAttachmentService _attachmentService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IProjectMemberService _memberService;
    private readonly ApplicationDbContext _context;

    public CommentController(
        ICommentService commentService,
        IAttachmentService attachmentService,
        IFileStorageService fileStorageService,
        IProjectMemberService memberService,
        ApplicationDbContext context)
    {
        _commentService = commentService;
        _attachmentService = attachmentService;
        _fileStorageService = fileStorageService;
        _memberService = memberService;
        _context = context;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<List<CommentViewModel>> BuildCommentViewModels(int taskItemId, string userId)
    {
        var commentsResult = await _commentService.GetCommentsForTask(taskItemId, userId);
        if (!commentsResult.Succeeded) return [];

        var viewModels = new List<CommentViewModel>();
        foreach (var c in commentsResult.Data!)
        {
            var attachmentsResult = await _attachmentService.GetAttachmentsAsync(AttachmentEntityType.Comment, c.Id);
            var commentVm = new CommentViewModel
            {
                Id = c.Id,
                TaskItemId = c.TaskItemId,
                AuthorId = c.AuthorId,
                AuthorDisplayName = c.Author.DisplayName,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                CanEditOrDelete = c.AuthorId == userId,
                Attachments = new AttachmentListViewModel
                {
                    EntityType = AttachmentEntityType.Comment,
                    EntityId = c.Id,
                    UploadUrl = Url.Action("UploadAttachment", "Comment", new { commentId = c.Id })!,
                    DownloadUrlTemplate = Url.Action("DownloadAttachment", "Comment", new { commentId = c.Id, attachmentId = "__ID__" })!,
                    DeleteUrlTemplate = Url.Action("DeleteAttachment", "Comment", new { commentId = c.Id, attachmentId = "__ID__" })!,
                    CanUpload = true,
                    Attachments = attachmentsResult.Succeeded
                        ? attachmentsResult.Data!.Select(a => new AttachmentViewModel
                        {
                            Id = a.Id,
                            FileName = a.FileName,
                            ContentType = a.ContentType,
                            FileSizeBytes = a.FileSizeBytes,
                            UploadedByDisplayName = a.UploadedByUser?.DisplayName ?? string.Empty,
                            UploadedAt = a.UploadedAt,
                            CanDelete = a.UploadedByUserId == userId || c.AuthorId == userId
                        }).ToList()
                        : []
                }
            };
            viewModels.Add(commentVm);
        }

        return viewModels;
    }

    private async Task<int?> GetProjectIdForComment(int commentId)
    {
        var comment = await _context.Comments
            .Include(c => c.TaskItem)
            .FirstOrDefaultAsync(c => c.Id == commentId);
        return comment?.TaskItem.ProjectId;
    }

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
            var viewModels = await BuildCommentViewModels(model.TaskItemId, GetUserId());
            return PartialView("_CommentList", viewModels);
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
            var viewModels = await BuildCommentViewModels(model.TaskItemId, GetUserId());
            return PartialView("_CommentList", viewModels);
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
            var viewModels = await BuildCommentViewModels(taskItemId, GetUserId());
            return PartialView("_CommentList", viewModels);
        }

        return RedirectToAction("Details", "Task", new { id = taskItemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachment(int commentId, IFormFile file)
    {
        var comment = await _context.Comments
            .Include(c => c.TaskItem)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment is null)
        {
            TempData["ErrorMessage"] = "Comment not found.";
            return RedirectToAction("Index", "Project");
        }

        if (file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a file to upload.";
            return RedirectToAction("Details", "Task", new { id = comment.TaskItemId });
        }

        var userId = GetUserId();
        var memberIds = await _memberService.GetMemberUserIds(comment.TaskItem.ProjectId);
        if (!memberIds.Contains(userId) && !User.IsInRole("Admin"))
        {
            TempData["ErrorMessage"] = "You must be a project member to upload attachments.";
            return RedirectToAction("Details", "Task", new { id = comment.TaskItemId });
        }

        using var stream = file.OpenReadStream();
        var result = await _attachmentService.UploadAsync(
            stream, file.FileName, file.ContentType, file.Length,
            AttachmentEntityType.Comment, commentId, userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "File uploaded successfully.";
        }

        return RedirectToAction("Details", "Task", new { id = comment.TaskItemId });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int commentId, int attachmentId)
    {
        var comment = await _context.Comments
            .Include(c => c.TaskItem)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment is null)
        {
            TempData["ErrorMessage"] = "Comment not found.";
            return RedirectToAction("Index", "Project");
        }

        var userId = GetUserId();
        var memberIds = await _memberService.GetMemberUserIds(comment.TaskItem.ProjectId);
        if (!memberIds.Contains(userId) && !User.IsInRole("Admin"))
        {
            TempData["ErrorMessage"] = "You must be a project member to download attachments.";
            return RedirectToAction("Details", "Task", new { id = comment.TaskItemId });
        }

        var result = await _attachmentService.GetByIdAsync(attachmentId);
        if (!result.Succeeded || result.Data!.EntityType != AttachmentEntityType.Comment || result.Data.EntityId != commentId)
        {
            TempData["ErrorMessage"] = "Attachment not found.";
            return RedirectToAction("Details", "Task", new { id = comment.TaskItemId });
        }

        var attachment = result.Data;
        var stream = await _fileStorageService.GetFileStreamAsync(attachment.StoredFileName);
        return File(stream, attachment.ContentType, attachment.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(int commentId, int attachmentId)
    {
        var comment = await _context.Comments
            .Include(c => c.TaskItem)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment is null)
        {
            TempData["ErrorMessage"] = "Comment not found.";
            return RedirectToAction("Index", "Project");
        }

        var userId = GetUserId();
        var attachmentResult = await _attachmentService.GetByIdAsync(attachmentId);
        if (!attachmentResult.Succeeded || attachmentResult.Data!.EntityType != AttachmentEntityType.Comment || attachmentResult.Data.EntityId != commentId)
        {
            TempData["ErrorMessage"] = "Attachment not found.";
            return RedirectToAction("Details", "Task", new { id = comment.TaskItemId });
        }

        var attachment = attachmentResult.Data;
        if (attachment.UploadedByUserId != userId && comment.AuthorId != userId && !User.IsInRole("Admin"))
        {
            TempData["ErrorMessage"] = "You do not have permission to delete this attachment.";
            return RedirectToAction("Details", "Task", new { id = comment.TaskItemId });
        }

        var result = await _attachmentService.DeleteAsync(attachmentId, userId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Attachment deleted.";
        }

        return RedirectToAction("Details", "Task", new { id = comment.TaskItemId });
    }
}
