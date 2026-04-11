using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public class WhiteboardChatService : IWhiteboardChatService
{
    private readonly ApplicationDbContext _context;

    public WhiteboardChatService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<WhiteboardChatMessageDto>> SendMessageAsync(int whiteboardId, string userId, string content)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<WhiteboardChatMessageDto>.Failure("User ID is required.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return ServiceResult<WhiteboardChatMessageDto>.Failure("Message content is required.");
        }

        var trimmedContent = content.Trim();
        if (trimmedContent.Length > 4000)
        {
            return ServiceResult<WhiteboardChatMessageDto>.Failure("Message content cannot exceed 4000 characters.");
        }

        var whiteboard = await _context.Whiteboards
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == whiteboardId);

        if (whiteboard == null)
        {
            return ServiceResult<WhiteboardChatMessageDto>.Failure("Whiteboard not found.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return ServiceResult<WhiteboardChatMessageDto>.Failure("User not found.");
        }

        var message = new WhiteboardChatMessage
        {
            WhiteboardId = whiteboardId,
            UserId = userId,
            Content = trimmedContent,
            CreatedAt = DateTime.UtcNow
        };

        _context.WhiteboardChatMessages.Add(message);
        await _context.SaveChangesAsync();

        return ServiceResult<WhiteboardChatMessageDto>.Success(new WhiteboardChatMessageDto
        {
            Id = message.Id,
            WhiteboardId = message.WhiteboardId,
            UserId = message.UserId,
            UserDisplayName = user.DisplayName,
            Content = message.Content,
            CreatedAt = message.CreatedAt
        });
    }

    public async Task<ServiceResult<List<WhiteboardChatMessageDto>>> GetMessagesAsync(int whiteboardId, int page, int pageSize)
    {
        if (page < 1)
        {
            page = 1;
        }

        pageSize = Math.Clamp(pageSize, 1, 100);

        var whiteboardExists = await _context.Whiteboards
            .AsNoTracking()
            .AnyAsync(w => w.Id == whiteboardId);

        if (!whiteboardExists)
        {
            return ServiceResult<List<WhiteboardChatMessageDto>>.Failure("Whiteboard not found.");
        }

        var messages = await _context.WhiteboardChatMessages
            .AsNoTracking()
            .Where(m => m.WhiteboardId == whiteboardId)
            .Include(m => m.User)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new WhiteboardChatMessageDto
            {
                Id = m.Id,
                WhiteboardId = m.WhiteboardId,
                UserId = m.UserId,
                UserDisplayName = m.User.DisplayName,
                Content = m.Content,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<WhiteboardChatMessageDto>>.Success(messages);
    }
}
