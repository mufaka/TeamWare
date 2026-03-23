using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Hubs;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class LoungeController : Controller
{
    private readonly ILoungeService _loungeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHubContext<LoungeHub> _hubContext;
    private readonly IAttachmentService _attachmentService;
    private readonly IFileStorageService _fileStorageService;

    public LoungeController(
        ILoungeService loungeService,
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IHubContext<LoungeHub> hubContext,
        IAttachmentService attachmentService,
        IFileStorageService fileStorageService)
    {
        _loungeService = loungeService;
        _dbContext = dbContext;
        _userManager = userManager;
        _hubContext = hubContext;
        _attachmentService = attachmentService;
        _fileStorageService = fileStorageService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> Room(int? projectId)
    {
        var userId = GetUserId();

        // Authorize: project members for project rooms, any authenticated user for #general
        if (projectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                return Forbid();
            }
        }

        var roomName = "#general";
        if (projectId.HasValue)
        {
            var project = await _dbContext.Projects.FindAsync(projectId.Value);
            if (project == null)
            {
                return NotFound();
            }
            roomName = project.Name;
        }

        // Load initial messages
        var messagesResult = await _loungeService.GetMessages(projectId, null, 50);
        var pinnedResult = await _loungeService.GetPinnedMessages(projectId);
        var readPositionResult = await _loungeService.GetReadPosition(userId, projectId);

        // Determine user's role in project for authorization
        var isProjectAdminOrOwner = false;
        if (projectId.HasValue)
        {
            var member = await _dbContext.ProjectMembers
                .FirstOrDefaultAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            isProjectAdminOrOwner = member != null && (member.Role == ProjectRole.Admin || member.Role == ProjectRole.Owner);
        }
        var isSiteAdmin = IsAdmin();

        // Load members for mention autocomplete
        var members = await GetRoomMembers(projectId);

        var viewModel = new LoungeRoomViewModel
        {
            ProjectId = projectId,
            RoomName = roomName,
            Messages = await MapMessagesAsync(messagesResult.Data ?? new List<LoungeMessage>(), userId, isProjectAdminOrOwner, isSiteAdmin, projectId.HasValue),
            PinnedMessages = await MapMessagesAsync(pinnedResult.Data ?? new List<LoungeMessage>(), userId, isProjectAdminOrOwner, isSiteAdmin, projectId.HasValue),
            LastReadMessageId = readPositionResult.Data,
            CanCreateTask = projectId.HasValue,
            Members = members
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Messages(int? projectId, DateTime? before, int count = 50)
    {
        var userId = GetUserId();

        // SEC-04: Clamp count to a reasonable range
        count = Math.Clamp(count, 1, 100);

        // Authorize
        if (projectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                return Forbid();
            }
        }

        var result = await _loungeService.GetMessages(projectId, before, count);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var isProjectAdminOrOwner = false;
        if (projectId.HasValue)
        {
            var member = await _dbContext.ProjectMembers
                .FirstOrDefaultAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            isProjectAdminOrOwner = member != null && (member.Role == ProjectRole.Admin || member.Role == ProjectRole.Owner);
        }
        var isSiteAdmin = IsAdmin();

        var viewModels = await MapMessagesAsync(result.Data!, userId, isProjectAdminOrOwner, isSiteAdmin, projectId.HasValue);

        return PartialView("_MessageList", viewModels);
    }

    [HttpGet]
    public async Task<IActionResult> PinnedMessages(int? projectId)
    {
        var userId = GetUserId();

        // Authorize
        if (projectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                return Forbid();
            }
        }

        var result = await _loungeService.GetPinnedMessages(projectId);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var isProjectAdminOrOwner = false;
        if (projectId.HasValue)
        {
            var member = await _dbContext.ProjectMembers
                .FirstOrDefaultAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            isProjectAdminOrOwner = member != null && (member.Role == ProjectRole.Admin || member.Role == ProjectRole.Owner);
        }
        var isSiteAdmin = IsAdmin();

        var viewModels = await MapMessagesAsync(result.Data!, userId, isProjectAdminOrOwner, isSiteAdmin, projectId.HasValue);

        return PartialView("_PinnedMessages", viewModels);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTaskFromMessage(int messageId)
    {
        var userId = GetUserId();

        var result = await _loungeService.CreateTaskFromMessage(messageId, userId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();

            if (Request.Headers["HX-Request"] == "true")
            {
                Response.StatusCode = 422;
                return Content(result.Errors.FirstOrDefault() ?? "Failed to create task.");
            }
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var task = result.Data!;

        // Get the message to find the room for broadcasting
        var messageResult = await _loungeService.GetMessage(messageId);
        if (messageResult.Succeeded)
        {
            var groupName = LoungeHub.GetRoomGroupName(messageResult.Data!.ProjectId);
            await _hubContext.Clients.Group(groupName).SendAsync("TaskCreatedFromMessage", new
            {
                MessageId = messageId,
                TaskId = task.Id,
                TaskTitle = task.Title
            });
        }

        if (Request.Headers["HX-Request"] == "true")
        {
            return Content($"<span class=\"text-xs text-green-600 dark:text-green-400\">Task #{task.Id} created</span>", "text/html");
        }

        return RedirectToAction("Details", "Task", new { id = task.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PinMessage(int messageId)
    {
        var userId = GetUserId();
        var isSiteAdmin = IsAdmin();

        var result = await _loungeService.PinMessage(messageId, userId, isSiteAdmin);
        if (!result.Succeeded)
        {
            if (Request.Headers["HX-Request"] == "true")
            {
                Response.StatusCode = 422;
                return Content(result.Errors.FirstOrDefault() ?? "Failed to pin message.");
            }
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var message = result.Data!;
        var groupName = LoungeHub.GetRoomGroupName(message.ProjectId);
        await _hubContext.Clients.Group(groupName).SendAsync("MessagePinned", new
        {
            message.Id,
            message.IsPinned,
            message.PinnedAt,
            PinnedByDisplayName = message.PinnedByUser?.DisplayName
        });

        if (Request.Headers["HX-Request"] == "true")
        {
            return await PinnedMessages(message.ProjectId);
        }

        return RedirectToAction("Room", new { projectId = message.ProjectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnpinMessage(int messageId)
    {
        var userId = GetUserId();
        var isSiteAdmin = IsAdmin();

        var result = await _loungeService.UnpinMessage(messageId, userId, isSiteAdmin);
        if (!result.Succeeded)
        {
            if (Request.Headers["HX-Request"] == "true")
            {
                Response.StatusCode = 422;
                return Content(result.Errors.FirstOrDefault() ?? "Failed to unpin message.");
            }
            return BadRequest(result.Errors.FirstOrDefault());
        }

        var message = result.Data!;
        var groupName = LoungeHub.GetRoomGroupName(message.ProjectId);
        await _hubContext.Clients.Group(groupName).SendAsync("MessageUnpinned", new
        {
            message.Id
        });

        if (Request.Headers["HX-Request"] == "true")
        {
            return await PinnedMessages(message.ProjectId);
        }

        return RedirectToAction("Room", new { projectId = message.ProjectId });
    }

    [HttpGet]
    public async Task<IActionResult> MemberSearch(int? projectId, string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length > 100)
        {
            return Json(Array.Empty<object>());
        }

        var userId = GetUserId();

        // Authorize: must have room access to search members
        if (projectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                return Forbid();
            }
        }

        var members = await GetRoomMembers(projectId);
        var filtered = members
            .Where(m => m.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || m.UserName.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .Select(m => new { m.UserId, m.DisplayName, m.UserName })
            .ToList();

        return Json(filtered);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachment(int messageId, IFormFile file)
    {
        var message = await _dbContext.LoungeMessages.FindAsync(messageId);
        if (message is null)
        {
            TempData["ErrorMessage"] = "Message not found.";
            return RedirectToAction("Room");
        }

        if (file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a file to upload.";
            return RedirectToAction("Room", new { projectId = message.ProjectId });
        }

        var userId = GetUserId();

        // Authorization: project members for project rooms, any authenticated user for #general
        if (message.ProjectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == message.ProjectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                TempData["ErrorMessage"] = "You must be a project member to upload attachments.";
                return RedirectToAction("Room", new { projectId = message.ProjectId });
            }
        }

        using var stream = file.OpenReadStream();
        var result = await _attachmentService.UploadAsync(
            stream, file.FileName, file.ContentType, file.Length,
            AttachmentEntityType.LoungeMessage, messageId, userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "File uploaded successfully.";

            // Broadcast attachment event to other users in the room
            var groupName = LoungeHub.GetRoomGroupName(message.ProjectId);
            var attachment = result.Data!;
            var user = await _userManager.FindByIdAsync(userId);
            await _hubContext.Clients.Group(groupName).SendAsync("AttachmentUploaded", new
            {
                MessageId = messageId,
                AttachmentId = attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.FileSizeBytes,
                UploadedByDisplayName = user?.DisplayName ?? "Unknown",
                attachment.UploadedAt
            });
        }

        return RedirectToAction("Room", new { projectId = message.ProjectId });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int messageId, int attachmentId)
    {
        var message = await _dbContext.LoungeMessages.FindAsync(messageId);
        if (message is null)
        {
            TempData["ErrorMessage"] = "Message not found.";
            return RedirectToAction("Room");
        }

        var userId = GetUserId();

        // Authorization: project members for project rooms, any authenticated user for #general
        if (message.ProjectId.HasValue)
        {
            var isMember = await _dbContext.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == message.ProjectId.Value && pm.UserId == userId);
            if (!isMember && !IsAdmin())
            {
                TempData["ErrorMessage"] = "You must be a project member to download attachments.";
                return RedirectToAction("Room", new { projectId = message.ProjectId });
            }
        }

        var result = await _attachmentService.GetByIdAsync(attachmentId);
        if (!result.Succeeded || result.Data!.EntityType != AttachmentEntityType.LoungeMessage || result.Data.EntityId != messageId)
        {
            TempData["ErrorMessage"] = "Attachment not found.";
            return RedirectToAction("Room", new { projectId = message.ProjectId });
        }

        var attachment = result.Data;
        var stream = await _fileStorageService.GetFileStreamAsync(attachment.StoredFileName);
        return File(stream, attachment.ContentType, attachment.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(int messageId, int attachmentId)
    {
        var message = await _dbContext.LoungeMessages.FindAsync(messageId);
        if (message is null)
        {
            TempData["ErrorMessage"] = "Message not found.";
            return RedirectToAction("Room");
        }

        var userId = GetUserId();
        var attachmentResult = await _attachmentService.GetByIdAsync(attachmentId);
        if (!attachmentResult.Succeeded || attachmentResult.Data!.EntityType != AttachmentEntityType.LoungeMessage || attachmentResult.Data.EntityId != messageId)
        {
            TempData["ErrorMessage"] = "Attachment not found.";
            return RedirectToAction("Room", new { projectId = message.ProjectId });
        }

        var attachment = attachmentResult.Data;
        var isProjectAdminOrOwner = false;
        if (message.ProjectId.HasValue)
        {
            var member = await _dbContext.ProjectMembers
                .FirstOrDefaultAsync(pm => pm.ProjectId == message.ProjectId.Value && pm.UserId == userId);
            isProjectAdminOrOwner = member != null && (member.Role == ProjectRole.Admin || member.Role == ProjectRole.Owner);
        }

        if (attachment.UploadedByUserId != userId && !isProjectAdminOrOwner && !IsAdmin())
        {
            TempData["ErrorMessage"] = "You do not have permission to delete this attachment.";
            return RedirectToAction("Room", new { projectId = message.ProjectId });
        }

        var result = await _attachmentService.DeleteAsync(attachmentId, userId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Attachment deleted.";

            // Broadcast deletion event
            var groupName = LoungeHub.GetRoomGroupName(message.ProjectId);
            await _hubContext.Clients.Group(groupName).SendAsync("AttachmentDeleted", new
            {
                MessageId = messageId,
                AttachmentId = attachmentId
            });
        }

        return RedirectToAction("Room", new { projectId = message.ProjectId });
    }

    private async Task<List<LoungeMessageViewModel>> MapMessagesAsync(
        List<LoungeMessage> messages,
        string currentUserId,
        bool isProjectAdminOrOwner,
        bool isSiteAdmin,
        bool isProjectRoom)
    {
        var viewModels = new List<LoungeMessageViewModel>();
        foreach (var m in messages)
        {
            var attachmentsResult = await _attachmentService.GetAttachmentsAsync(AttachmentEntityType.LoungeMessage, m.Id);
            viewModels.Add(new LoungeMessageViewModel
            {
                Id = m.Id,
                ProjectId = m.ProjectId,
                AuthorId = m.UserId,
                AuthorDisplayName = m.User?.DisplayName ?? "Unknown",
                AuthorAvatarUrl = m.User?.AvatarUrl,
                Content = m.Content,
                CreatedAt = m.CreatedAt,
                IsEdited = m.IsEdited,
                EditedAt = m.EditedAt,
                IsPinned = m.IsPinned,
                PinnedByDisplayName = m.PinnedByUser?.DisplayName,
                PinnedAt = m.PinnedAt,
                CreatedTaskId = m.CreatedTaskId,
                Reactions = m.Reactions?.GroupBy(r => r.ReactionType)
                    .Select(g => new ReactionSummary
                    {
                        ReactionType = g.Key,
                        Count = g.Count(),
                        CurrentUserReacted = g.Any(r => r.UserId == currentUserId)
                    }).ToList() ?? new List<ReactionSummary>(),
                CanEdit = m.UserId == currentUserId,
                CanDelete = m.UserId == currentUserId || isProjectAdminOrOwner || isSiteAdmin,
                CanPin = isProjectAdminOrOwner || isSiteAdmin,
                CanCreateTask = isProjectRoom && m.CreatedTaskId == null,
                Attachments = new AttachmentListViewModel
                {
                    EntityType = AttachmentEntityType.LoungeMessage,
                    EntityId = m.Id,
                    UploadUrl = Url.Action("UploadAttachment", "Lounge", new { messageId = m.Id })!,
                    DownloadUrlTemplate = Url.Action("DownloadAttachment", "Lounge", new { messageId = m.Id, attachmentId = "__ID__" })!,
                    DeleteUrlTemplate = Url.Action("DeleteAttachment", "Lounge", new { messageId = m.Id, attachmentId = "__ID__" })!,
                    CanUpload = false, // Upload is handled via the room input area, not per-message
                    Attachments = attachmentsResult.Succeeded
                        ? attachmentsResult.Data!.Select(a => new AttachmentViewModel
                        {
                            Id = a.Id,
                            FileName = a.FileName,
                            ContentType = a.ContentType,
                            FileSizeBytes = a.FileSizeBytes,
                            UploadedByDisplayName = a.UploadedByUser?.DisplayName ?? string.Empty,
                            UploadedAt = a.UploadedAt,
                            CanDelete = a.UploadedByUserId == currentUserId || isProjectAdminOrOwner || isSiteAdmin
                        }).ToList()
                        : []
                }
            });
        }
        return viewModels;
    }

    private async Task<List<LoungeMemberViewModel>> GetRoomMembers(int? projectId)
    {
        if (projectId.HasValue)
        {
            return await _dbContext.ProjectMembers
                .Where(pm => pm.ProjectId == projectId.Value)
                .Select(pm => new LoungeMemberViewModel
                {
                    UserId = pm.UserId,
                    DisplayName = pm.User.DisplayName,
                    UserName = pm.User.UserName ?? string.Empty
                })
                .ToListAsync();
        }

        // #general — all users
        return await _dbContext.Users
            .Select(u => new LoungeMemberViewModel
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                UserName = u.UserName ?? string.Empty
            })
            .ToListAsync();
    }
}
