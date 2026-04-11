using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public class WhiteboardService : IWhiteboardService
{
    private readonly ApplicationDbContext _context;

    public WhiteboardService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<int>> CreateAsync(string userId, string title)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<int>.Failure("User ID is required.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return ServiceResult<int>.Failure("Whiteboard title is required.");
        }

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > 200)
        {
            return ServiceResult<int>.Failure("Whiteboard title must be 200 characters or fewer.");
        }

        var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
        {
            return ServiceResult<int>.Failure("User not found.");
        }

        var whiteboard = new Whiteboard
        {
            Title = trimmedTitle,
            OwnerId = userId,
            CurrentPresenterId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Whiteboards.Add(whiteboard);
        await _context.SaveChangesAsync();

        return ServiceResult<int>.Success(whiteboard.Id);
    }

    public async Task<ServiceResult<WhiteboardDetailDto?>> GetByIdAsync(int whiteboardId)
    {
        var whiteboard = await _context.Whiteboards
            .AsNoTracking()
            .Include(w => w.Owner)
            .Include(w => w.Project)
            .Include(w => w.CurrentPresenter)
            .Include(w => w.Invitations)
                .ThenInclude(i => i.User)
            .FirstOrDefaultAsync(w => w.Id == whiteboardId);

        if (whiteboard == null)
        {
            return ServiceResult<WhiteboardDetailDto?>.Failure("Whiteboard not found.");
        }

        return ServiceResult<WhiteboardDetailDto?>.Success(MapToDetailDto(whiteboard));
    }

    public async Task<ServiceResult<List<WhiteboardDto>>> GetLandingPageAsync(string userId, bool isSiteAdmin)
    {
        if (!isSiteAdmin && string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<List<WhiteboardDto>>.Failure("User ID is required.");
        }

        var query = _context.Whiteboards
            .AsNoTracking()
            .Include(w => w.Owner)
            .Include(w => w.Project)
            .Include(w => w.CurrentPresenter)
            .AsQueryable();

        if (!isSiteAdmin)
        {
            query = query.Where(w =>
                w.OwnerId == userId ||
                (!w.ProjectId.HasValue && w.Invitations.Any(i => i.UserId == userId)) ||
                (w.ProjectId.HasValue && _context.ProjectMembers.Any(pm => pm.ProjectId == w.ProjectId && pm.UserId == userId)));
        }

        var whiteboards = await query
            .OrderByDescending(w => w.CurrentPresenterId != null)
            .ThenByDescending(w => w.UpdatedAt)
            .ToListAsync();

        return ServiceResult<List<WhiteboardDto>>.Success(whiteboards.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult> DeleteAsync(int whiteboardId, string userId, bool isSiteAdmin)
    {
        var whiteboard = await _context.Whiteboards.FindAsync(whiteboardId);
        if (whiteboard == null)
        {
            return ServiceResult.Failure("Whiteboard not found.");
        }

        if (!isSiteAdmin && whiteboard.OwnerId != userId)
        {
            return ServiceResult.Failure("Only the whiteboard owner or a site admin can delete this whiteboard.");
        }

        _context.Whiteboards.Remove(whiteboard);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SaveCanvasAsync(int whiteboardId, string canvasData, string presenterId)
    {
        if (string.IsNullOrWhiteSpace(presenterId))
        {
            return ServiceResult.Failure("Presenter ID is required.");
        }

        if (string.IsNullOrWhiteSpace(canvasData))
        {
            return ServiceResult.Failure("Canvas data is required.");
        }

        if (canvasData.Length > 500_000)
        {
            return ServiceResult.Failure("Canvas data exceeds the maximum allowed size.");
        }

        if (ContainsDangerousCanvasMarkup(canvasData))
        {
            return ServiceResult.Failure("Canvas data contains unsafe content.");
        }

        if (!IsValidCanvasJson(canvasData))
        {
            return ServiceResult.Failure("Canvas data is invalid.");
        }

        var whiteboard = await _context.Whiteboards.FindAsync(whiteboardId);
        if (whiteboard == null)
        {
            return ServiceResult.Failure("Whiteboard not found.");
        }

        if (whiteboard.CurrentPresenterId != presenterId)
        {
            return ServiceResult.Failure("Only the current presenter can save canvas changes.");
        }

        whiteboard.CanvasData = canvasData;
        whiteboard.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<bool>> CanAccessAsync(int whiteboardId, string userId, bool isSiteAdmin)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<bool>.Failure("User ID is required.");
        }

        if (isSiteAdmin)
        {
            return ServiceResult<bool>.Success(true);
        }

        var whiteboard = await _context.Whiteboards
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == whiteboardId);

        if (whiteboard == null)
        {
            return ServiceResult<bool>.Failure("Whiteboard not found.");
        }

        if (whiteboard.OwnerId == userId)
        {
            return ServiceResult<bool>.Success(true);
        }

        if (whiteboard.ProjectId.HasValue)
        {
            var isProjectMember = await _context.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == whiteboard.ProjectId && pm.UserId == userId);

            return ServiceResult<bool>.Success(isProjectMember);
        }

        var hasInvitation = await _context.WhiteboardInvitations
            .AnyAsync(i => i.WhiteboardId == whiteboardId && i.UserId == userId);

        return ServiceResult<bool>.Success(hasInvitation);
    }

    private static bool ContainsDangerousCanvasMarkup(string canvasData)
    {
        return canvasData.Contains("<script", StringComparison.OrdinalIgnoreCase)
            || canvasData.Contains("</script", StringComparison.OrdinalIgnoreCase)
            || canvasData.Contains("<!--", StringComparison.OrdinalIgnoreCase)
            || canvasData.Contains("-->", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidCanvasJson(string canvasData)
    {
        try
        {
            using var document = JsonDocument.Parse(canvasData);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("shapes", out var shapesElement)
                && shapesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("viewport", out var viewportElement)
                && viewportElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static WhiteboardDto MapToDto(Whiteboard whiteboard)
    {
        return new WhiteboardDto
        {
            Id = whiteboard.Id,
            Title = whiteboard.Title,
            OwnerId = whiteboard.OwnerId,
            OwnerDisplayName = whiteboard.Owner.DisplayName,
            ProjectId = whiteboard.ProjectId,
            ProjectName = whiteboard.Project?.Name,
            CurrentPresenterId = whiteboard.CurrentPresenterId,
            CurrentPresenterDisplayName = whiteboard.CurrentPresenter?.DisplayName,
            CreatedAt = whiteboard.CreatedAt,
            UpdatedAt = whiteboard.UpdatedAt
        };
    }

    private static WhiteboardDetailDto MapToDetailDto(Whiteboard whiteboard)
    {
        return new WhiteboardDetailDto
        {
            Id = whiteboard.Id,
            Title = whiteboard.Title,
            OwnerId = whiteboard.OwnerId,
            OwnerDisplayName = whiteboard.Owner.DisplayName,
            ProjectId = whiteboard.ProjectId,
            ProjectName = whiteboard.Project?.Name,
            CurrentPresenterId = whiteboard.CurrentPresenterId,
            CurrentPresenterDisplayName = whiteboard.CurrentPresenter?.DisplayName,
            CreatedAt = whiteboard.CreatedAt,
            UpdatedAt = whiteboard.UpdatedAt,
            CanvasData = whiteboard.CanvasData,
            Invitations = whiteboard.Invitations
                .OrderBy(i => i.CreatedAt)
                .Select(i => new WhiteboardInvitationDto
                {
                    Id = i.Id,
                    WhiteboardId = i.WhiteboardId,
                    UserId = i.UserId,
                    UserDisplayName = i.User.DisplayName,
                    InvitedByUserId = i.InvitedByUserId,
                    CreatedAt = i.CreatedAt
                })
                .ToList(),
            ActiveUsers = new List<string>()
        };
    }
}
