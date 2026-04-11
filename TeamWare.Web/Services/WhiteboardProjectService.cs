using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class WhiteboardProjectService : IWhiteboardProjectService
{
    private readonly ApplicationDbContext _context;
    private readonly IWhiteboardInvitationService _whiteboardInvitationService;

    public WhiteboardProjectService(ApplicationDbContext context, IWhiteboardInvitationService whiteboardInvitationService)
    {
        _context = context;
        _whiteboardInvitationService = whiteboardInvitationService;
    }

    public async Task<ServiceResult> SaveToProjectAsync(int whiteboardId, int projectId, string userId)
    {
        var whiteboard = await _context.Whiteboards.FindAsync(whiteboardId);
        if (whiteboard == null)
        {
            return ServiceResult.Failure("Whiteboard not found.");
        }

        if (whiteboard.OwnerId != userId)
        {
            return ServiceResult.Failure("Only the whiteboard owner can save this whiteboard to a project.");
        }

        var project = await _context.Projects.FindAsync(projectId);
        if (project == null)
        {
            return ServiceResult.Failure("Project not found.");
        }

        var isProjectMember = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);

        if (!isProjectMember)
        {
            return ServiceResult.Failure("Only project members can save a whiteboard to this project.");
        }

        whiteboard.ProjectId = projectId;
        whiteboard.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return await _whiteboardInvitationService.CleanupInvalidInvitationsAsync(whiteboardId);
    }

    public async Task<ServiceResult> ClearProjectAsync(int whiteboardId, string userId)
    {
        var whiteboard = await _context.Whiteboards.FindAsync(whiteboardId);
        if (whiteboard == null)
        {
            return ServiceResult.Failure("Whiteboard not found.");
        }

        if (whiteboard.OwnerId != userId)
        {
            return ServiceResult.Failure("Only the whiteboard owner can clear the project association.");
        }

        whiteboard.ProjectId = null;
        whiteboard.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> TransferOwnershipIfNeededAsync(int whiteboardId)
    {
        var whiteboard = await _context.Whiteboards.FindAsync(whiteboardId);
        if (whiteboard == null)
        {
            return ServiceResult.Failure("Whiteboard not found.");
        }

        if (!whiteboard.ProjectId.HasValue)
        {
            return ServiceResult.Success();
        }

        var ownerIsMember = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == whiteboard.ProjectId && pm.UserId == whiteboard.OwnerId);

        if (ownerIsMember)
        {
            return ServiceResult.Success();
        }

        var projectOwner = await _context.ProjectMembers
            .Where(pm => pm.ProjectId == whiteboard.ProjectId && pm.Role == ProjectRole.Owner)
            .Select(pm => pm.UserId)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(projectOwner))
        {
            return ServiceResult.Failure("Project owner not found.");
        }

        whiteboard.OwnerId = projectOwner;
        whiteboard.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }
}
