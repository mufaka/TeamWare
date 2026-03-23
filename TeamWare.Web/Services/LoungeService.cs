using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public partial class LoungeService : ILoungeService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;

    private static readonly HashSet<string> AllowedReactionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbsup",
        "heart",
        "laugh",
        "rocket",
        "eyes"
    };

    [GeneratedRegex(@"(?<!\w)@([\w.+-]+@[\w.-]+\.\w+|[\w]+)", RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    public LoungeService(ApplicationDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    // --- Authorization helpers ---

    private async Task<bool> IsProjectMember(int projectId, string userId)
    {
        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
    }

    private async Task<bool> IsProjectOwnerOrAdmin(int projectId, string userId)
    {
        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId
                         && pm.UserId == userId
                         && (pm.Role == ProjectRole.Owner || pm.Role == ProjectRole.Admin));
    }

    private async Task<bool> CanAccessRoom(int? projectId, string userId)
    {
        if (projectId == null)
        {
            // #general — any authenticated user
            return true;
        }

        return await IsProjectMember(projectId.Value, userId);
    }

    // --- Mention parsing helpers (Phase 19) ---

    /// <summary>
    /// Extracts @username mentions from message content (LOUNGE-20).
    /// </summary>
    public static List<string> ExtractMentionedUsernames(string content)
    {
        var matches = MentionRegex().Matches(content);
        return matches
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolves mentioned usernames to users, filters to room members,
    /// excludes self-mentions, and creates LoungeMention notifications (LOUNGE-21, LOUNGE-22, LOUNGE-23).
    /// </summary>
    private async Task ProcessMentions(LoungeMessage message)
    {
        var usernames = ExtractMentionedUsernames(message.Content);
        if (usernames.Count == 0) return;

        // Resolve usernames to actual users
        var mentionedUsers = await _context.Users
            .Where(u => u.UserName != null && usernames.Contains(u.UserName))
            .ToListAsync();

        if (mentionedUsers.Count == 0) return;

        // Filter to room members only (LOUNGE-21)
        List<ApplicationUser> validMentionedUsers;
        if (message.ProjectId != null)
        {
            // Project room: only project members
            var memberUserIds = await _context.ProjectMembers
                .Where(pm => pm.ProjectId == message.ProjectId)
                .Select(pm => pm.UserId)
                .ToListAsync();

            validMentionedUsers = mentionedUsers
                .Where(u => memberUserIds.Contains(u.Id))
                .ToList();
        }
        else
        {
            // #general: all authenticated users are valid
            validMentionedUsers = mentionedUsers;
        }

        // Exclude self-mentions
        validMentionedUsers = validMentionedUsers
            .Where(u => u.Id != message.UserId)
            .ToList();

        // Determine room name for notification message
        string roomName;
        if (message.ProjectId != null)
        {
            var project = await _context.Projects.FindAsync(message.ProjectId);
            roomName = project?.Name ?? "a project lounge";
        }
        else
        {
            roomName = "#general";
        }

        var senderDisplayName = message.User?.DisplayName ?? "Someone";

        // Create notifications for each mentioned user (LOUNGE-22, LOUNGE-23)
        foreach (var user in validMentionedUsers)
        {
            var notificationMessage = $"{senderDisplayName} mentioned you in {roomName}";
            await _notificationService.CreateNotification(
                user.Id,
                notificationMessage,
                NotificationType.LoungeMention,
                message.Id);
        }
    }

    // --- 16.1 Message Services ---

    public async Task<ServiceResult<LoungeMessage>> SendMessage(int? projectId, string userId, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ServiceResult<LoungeMessage>.Failure("Message content is required.");
        }

        if (content.Length > 4000)
        {
            return ServiceResult<LoungeMessage>.Failure("Message content cannot exceed 4000 characters.");
        }

        if (!await CanAccessRoom(projectId, userId))
        {
            return ServiceResult<LoungeMessage>.Failure("You do not have access to this room.");
        }

        var message = new LoungeMessage
        {
            ProjectId = projectId,
            UserId = userId,
            Content = content.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        await _context.Entry(message).Reference(m => m.User).LoadAsync();

        // Phase 19: Parse @mentions and create notifications
        await ProcessMentions(message);

        return ServiceResult<LoungeMessage>.Success(message);
    }

    public async Task<ServiceResult<LoungeMessage>> EditMessage(int messageId, string userId, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ServiceResult<LoungeMessage>.Failure("Message content is required.");
        }

        if (content.Length > 4000)
        {
            return ServiceResult<LoungeMessage>.Failure("Message content cannot exceed 4000 characters.");
        }

        var message = await _context.LoungeMessages.FindAsync(messageId);
        if (message == null)
        {
            return ServiceResult<LoungeMessage>.Failure("Message not found.");
        }

        // LOUNGE-64: Only the author can edit their own messages
        if (message.UserId != userId)
        {
            return ServiceResult<LoungeMessage>.Failure("You can only edit your own messages.");
        }

        message.Content = content.Trim();
        message.IsEdited = true;
        message.EditedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _context.Entry(message).Reference(m => m.User).LoadAsync();

        return ServiceResult<LoungeMessage>.Success(message);
    }

    public async Task<ServiceResult> DeleteMessage(int messageId, string userId, bool isSiteAdmin = false)
    {
        var message = await _context.LoungeMessages.FindAsync(messageId);
        if (message == null)
        {
            return ServiceResult.Failure("Message not found.");
        }

        // LOUNGE-65: In project rooms, author OR project owner/admin can delete
        // LOUNGE-66: In #general, author OR site admin can delete
        bool isAuthor = message.UserId == userId;

        if (!isAuthor)
        {
            if (message.ProjectId != null)
            {
                if (!await IsProjectOwnerOrAdmin(message.ProjectId.Value, userId))
                {
                    return ServiceResult.Failure("You do not have permission to delete this message.");
                }
            }
            else
            {
                if (!isSiteAdmin)
                {
                    return ServiceResult.Failure("You do not have permission to delete this message.");
                }
            }
        }

        _context.LoungeMessages.Remove(message);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<LoungeMessage>>> GetMessages(int? projectId, DateTime? before, int count)
    {
        count = Math.Clamp(count, 1, 100);

        var query = _context.LoungeMessages
            .Where(m => m.ProjectId == projectId)
            .Include(m => m.User)
            .Include(m => m.Reactions)
            .AsQueryable();

        if (before.HasValue)
        {
            query = query.Where(m => m.CreatedAt < before.Value);
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .ToListAsync();

        // Return in chronological order
        messages.Reverse();

        return ServiceResult<List<LoungeMessage>>.Success(messages);
    }

    public async Task<ServiceResult<LoungeMessage>> GetMessage(int messageId)
    {
        var message = await _context.LoungeMessages
            .Include(m => m.User)
            .Include(m => m.Reactions)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
        {
            return ServiceResult<LoungeMessage>.Failure("Message not found.");
        }

        return ServiceResult<LoungeMessage>.Success(message);
    }

    // --- 16.2 Pin Services ---

    public async Task<ServiceResult<LoungeMessage>> PinMessage(int messageId, string userId, bool isSiteAdmin = false)
    {
        var message = await _context.LoungeMessages.FindAsync(messageId);
        if (message == null)
        {
            return ServiceResult<LoungeMessage>.Failure("Message not found.");
        }

        if (message.IsPinned)
        {
            return ServiceResult<LoungeMessage>.Failure("Message is already pinned.");
        }

        // LOUNGE-67: Project rooms — project owner/admin can pin
        // LOUNGE-68: #general — site admin can pin
        if (message.ProjectId != null)
        {
            if (!await IsProjectOwnerOrAdmin(message.ProjectId.Value, userId))
            {
                return ServiceResult<LoungeMessage>.Failure("Only project owners and admins can pin messages.");
            }
        }
        else
        {
            if (!isSiteAdmin)
            {
                return ServiceResult<LoungeMessage>.Failure("Only site admins can pin messages in #general.");
            }
        }

        message.IsPinned = true;
        message.PinnedByUserId = userId;
        message.PinnedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _context.Entry(message).Reference(m => m.User).LoadAsync();

        return ServiceResult<LoungeMessage>.Success(message);
    }

    public async Task<ServiceResult<LoungeMessage>> UnpinMessage(int messageId, string userId, bool isSiteAdmin = false)
    {
        var message = await _context.LoungeMessages.FindAsync(messageId);
        if (message == null)
        {
            return ServiceResult<LoungeMessage>.Failure("Message not found.");
        }

        if (!message.IsPinned)
        {
            return ServiceResult<LoungeMessage>.Failure("Message is not pinned.");
        }

        // LOUNGE-67: Project rooms — project owner/admin can unpin
        // LOUNGE-68: #general — site admin can unpin
        if (message.ProjectId != null)
        {
            if (!await IsProjectOwnerOrAdmin(message.ProjectId.Value, userId))
            {
                return ServiceResult<LoungeMessage>.Failure("Only project owners and admins can unpin messages.");
            }
        }
        else
        {
            if (!isSiteAdmin)
            {
                return ServiceResult<LoungeMessage>.Failure("Only site admins can unpin messages in #general.");
            }
        }

        message.IsPinned = false;
        message.PinnedByUserId = null;
        message.PinnedAt = null;

        await _context.SaveChangesAsync();

        await _context.Entry(message).Reference(m => m.User).LoadAsync();

        return ServiceResult<LoungeMessage>.Success(message);
    }

    public async Task<ServiceResult<List<LoungeMessage>>> GetPinnedMessages(int? projectId)
    {
        var messages = await _context.LoungeMessages
            .Where(m => m.ProjectId == projectId && m.IsPinned)
            .Include(m => m.User)
            .OrderByDescending(m => m.PinnedAt)
            .ToListAsync();

        return ServiceResult<List<LoungeMessage>>.Success(messages);
    }

    // --- 16.3 Reaction Services ---

    public async Task<ServiceResult<LoungeReaction>> ToggleReaction(int messageId, string userId, string reactionType)
    {
        if (string.IsNullOrWhiteSpace(reactionType))
        {
            return ServiceResult<LoungeReaction>.Failure("Reaction type is required.");
        }

        if (!AllowedReactionTypes.Contains(reactionType))
        {
            return ServiceResult<LoungeReaction>.Failure(
                $"Invalid reaction type. Allowed types: {string.Join(", ", AllowedReactionTypes)}.");
        }

        var message = await _context.LoungeMessages.FindAsync(messageId);
        if (message == null)
        {
            return ServiceResult<LoungeReaction>.Failure("Message not found.");
        }

        // LOUNGE-69: Project rooms — must be a project member to react
        // LOUNGE-70: #general — any authenticated user can react
        if (message.ProjectId != null)
        {
            if (!await IsProjectMember(message.ProjectId.Value, userId))
            {
                return ServiceResult<LoungeReaction>.Failure("You must be a project member to react to messages.");
            }
        }

        var existing = await _context.LoungeReactions
            .FirstOrDefaultAsync(r => r.LoungeMessageId == messageId
                                   && r.UserId == userId
                                   && r.ReactionType == reactionType);

        if (existing != null)
        {
            // Remove the existing reaction (toggle off)
            _context.LoungeReactions.Remove(existing);
            await _context.SaveChangesAsync();

            // Return the removed reaction to indicate it was toggled off
            return ServiceResult<LoungeReaction>.Success(existing);
        }

        // Add a new reaction (toggle on)
        var reaction = new LoungeReaction
        {
            LoungeMessageId = messageId,
            UserId = userId,
            ReactionType = reactionType,
            CreatedAt = DateTime.UtcNow
        };

        _context.LoungeReactions.Add(reaction);
        await _context.SaveChangesAsync();

        return ServiceResult<LoungeReaction>.Success(reaction);
    }

    public async Task<ServiceResult<List<ReactionSummary>>> GetReactionsForMessage(int messageId, string? currentUserId = null)
    {
        var message = await _context.LoungeMessages.FindAsync(messageId);
        if (message == null)
        {
            return ServiceResult<List<ReactionSummary>>.Failure("Message not found.");
        }

        var reactions = await _context.LoungeReactions
            .Where(r => r.LoungeMessageId == messageId)
            .ToListAsync();

        var summaries = reactions
            .GroupBy(r => r.ReactionType)
            .Select(g => new ReactionSummary
            {
                ReactionType = g.Key,
                Count = g.Count(),
                CurrentUserReacted = currentUserId != null && g.Any(r => r.UserId == currentUserId)
            })
            .OrderBy(s => s.ReactionType)
            .ToList();

        return ServiceResult<List<ReactionSummary>>.Success(summaries);
    }

    // --- 16.4 Unread Tracking Services ---

    public async Task<ServiceResult> UpdateReadPosition(string userId, int? projectId, int lastReadMessageId)
    {
        var messageExists = await _context.LoungeMessages
            .AnyAsync(m => m.Id == lastReadMessageId && m.ProjectId == projectId);

        if (!messageExists)
        {
            return ServiceResult.Failure("Message not found in the specified room.");
        }

        var position = await _context.LoungeReadPositions
            .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.ProjectId == projectId);

        if (position != null)
        {
            // Only advance the read position, never go backward
            if (lastReadMessageId > position.LastReadMessageId)
            {
                position.LastReadMessageId = lastReadMessageId;
                position.UpdatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            position = new LoungeReadPosition
            {
                UserId = userId,
                ProjectId = projectId,
                LastReadMessageId = lastReadMessageId,
                UpdatedAt = DateTime.UtcNow
            };
            _context.LoungeReadPositions.Add(position);
        }

        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<RoomUnreadCount>>> GetUnreadCounts(string userId)
    {
        // Get all read positions for the user
        var readPositionList = await _context.LoungeReadPositions
            .Where(rp => rp.UserId == userId)
            .ToListAsync();

        // Build a lookup: ProjectId -> LastReadMessageId (handle null key for #general)
        int GetLastReadId(int? projectId)
        {
            foreach (var rp in readPositionList)
            {
                if (rp.ProjectId == projectId)
                    return rp.LastReadMessageId;
            }
            return 0;
        }

        // Get all rooms the user has access to (projects they're a member of + #general)
        var memberProjectIds = await _context.ProjectMembers
            .Where(pm => pm.UserId == userId)
            .Select(pm => (int?)pm.ProjectId)
            .ToListAsync();

        // Include #general (null projectId)
        var roomIds = new List<int?> { null };
        roomIds.AddRange(memberProjectIds);

        var unreadCounts = new List<RoomUnreadCount>();

        foreach (var roomId in roomIds)
        {
            var lastReadId = GetLastReadId(roomId);

            var unreadCount = await _context.LoungeMessages
                .CountAsync(m => m.ProjectId == roomId && m.Id > lastReadId);

            if (unreadCount > 0)
            {
                unreadCounts.Add(new RoomUnreadCount { ProjectId = roomId, Count = unreadCount });
            }
        }

        return ServiceResult<List<RoomUnreadCount>>.Success(unreadCounts);
    }

    public async Task<ServiceResult<int?>> GetReadPosition(string userId, int? projectId)
    {
        var position = await _context.LoungeReadPositions
            .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.ProjectId == projectId);

        return ServiceResult<int?>.Success(position?.LastReadMessageId);
    }

    // --- 16.5 Message-to-Task Conversion ---

    public async Task<ServiceResult<TaskItem>> CreateTaskFromMessage(int messageId, string userId)
    {
        var message = await _context.LoungeMessages
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
        {
            return ServiceResult<TaskItem>.Failure("Message not found.");
        }

        // LOUNGE-71: Only available in project rooms, not #general
        if (message.ProjectId == null)
        {
            return ServiceResult<TaskItem>.Failure("Tasks can only be created from messages in project rooms.");
        }

        // Must be a project member
        if (!await IsProjectMember(message.ProjectId.Value, userId))
        {
            return ServiceResult<TaskItem>.Failure("You must be a project member to create tasks.");
        }

        // Check if a task was already created from this message
        if (message.CreatedTaskId != null)
        {
            return ServiceResult<TaskItem>.Failure("A task has already been created from this message.");
        }

        // Pre-populate task from message content (LOUNGE-45, LOUNGE-46)
        var title = message.Content.Length > 300
            ? message.Content[..297] + "..."
            : message.Content;

        // Replace newlines with spaces for the title
        title = title.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

        var description = $"Created from lounge message by {message.User?.DisplayName ?? "Unknown"}:\n\n{message.Content}";
        if (description.Length > 4000)
        {
            description = description[..3997] + "...";
        }

        var task = new TaskItem
        {
            ProjectId = message.ProjectId.Value,
            Title = title,
            Description = description,
            Priority = TaskItemPriority.Medium,
            Status = TaskItemStatus.ToDo,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        // Link the message to the created task
        message.CreatedTaskId = task.Id;
        await _context.SaveChangesAsync();

        return ServiceResult<TaskItem>.Success(task);
    }

    // --- 16.6 Message Retention ---

    public async Task<List<int>> GetExpiredMessageIds()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);

        return await _context.LoungeMessages
            .Where(m => m.CreatedAt < cutoffDate && !m.IsPinned)
            .Select(m => m.Id)
            .ToListAsync();
    }

    public async Task<int> CleanupExpiredMessages()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);

        // Find non-pinned messages older than 30 days (LOUNGE-50, LOUNGE-51)
        var expiredMessages = await _context.LoungeMessages
            .Where(m => m.CreatedAt < cutoffDate && !m.IsPinned)
            .ToListAsync();

        if (expiredMessages.Count == 0)
        {
            return 0;
        }

        var expiredMessageIds = expiredMessages.Select(m => m.Id).ToList();

        // Cascade delete reactions (LOUNGE-53) — EF cascade handles this automatically,
        // but we also clean up orphaned read positions (LOUNGE-54, LOUNGE-55)
        var orphanedReadPositions = await _context.LoungeReadPositions
            .Where(rp => expiredMessageIds.Contains(rp.LastReadMessageId))
            .ToListAsync();

        // For orphaned read positions, try to find the next most recent message in the same room
        foreach (var readPosition in orphanedReadPositions)
        {
            var nextMessage = await _context.LoungeMessages
                .Where(m => m.ProjectId == readPosition.ProjectId
                         && m.Id < readPosition.LastReadMessageId
                         && !expiredMessageIds.Contains(m.Id))
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            if (nextMessage != null)
            {
                readPosition.LastReadMessageId = nextMessage.Id;
                readPosition.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // No remaining messages — remove the read position
                _context.LoungeReadPositions.Remove(readPosition);
            }
        }

        _context.LoungeMessages.RemoveRange(expiredMessages);
        await _context.SaveChangesAsync();

        return expiredMessages.Count;
    }
}
