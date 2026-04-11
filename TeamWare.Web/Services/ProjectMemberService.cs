using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class ProjectMemberService : IProjectMemberService
{
    private readonly ApplicationDbContext _context;
    private readonly IWhiteboardProjectService? _whiteboardProjectService;

    public ProjectMemberService(ApplicationDbContext context, IWhiteboardProjectService? whiteboardProjectService = null)
    {
        _context = context;
        _whiteboardProjectService = whiteboardProjectService;
    }

    public async Task<ServiceResult<ProjectMember>> InviteMember(int projectId, string targetUserId, string invitedByUserId)
    {
        var project = await _context.Projects.FindAsync(projectId);
        if (project == null)
        {
            return ServiceResult<ProjectMember>.Failure("Project not found.");
        }

        var inviterMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == invitedByUserId);

        if (inviterMembership == null || (inviterMembership.Role != ProjectRole.Owner && inviterMembership.Role != ProjectRole.Admin))
        {
            return ServiceResult<ProjectMember>.Failure("Only project owners and admins can invite members.");
        }

        var targetUser = await _context.Users.FindAsync(targetUserId);
        if (targetUser == null)
        {
            return ServiceResult<ProjectMember>.Failure("User not found.");
        }

        var existingMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == targetUserId);

        if (existingMembership != null)
        {
            return ServiceResult<ProjectMember>.Failure("User is already a member of this project.");
        }

        var newMember = new ProjectMember
        {
            ProjectId = projectId,
            UserId = targetUserId,
            Role = ProjectRole.Member,
            JoinedAt = DateTime.UtcNow
        };

        _context.ProjectMembers.Add(newMember);
        await _context.SaveChangesAsync();

        return ServiceResult<ProjectMember>.Success(newMember);
    }

    public async Task<ServiceResult> RemoveMember(int projectId, string targetUserId, string removedByUserId)
    {
        var removerMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == removedByUserId);

        if (removerMembership == null || (removerMembership.Role != ProjectRole.Owner && removerMembership.Role != ProjectRole.Admin))
        {
            return ServiceResult.Failure("Only project owners and admins can remove members.");
        }

        var targetMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == targetUserId);

        if (targetMembership == null)
        {
            return ServiceResult.Failure("User is not a member of this project.");
        }

        if (targetMembership.Role == ProjectRole.Owner)
        {
            return ServiceResult.Failure("Cannot remove the project owner.");
        }

        _context.ProjectMembers.Remove(targetMembership);
        await _context.SaveChangesAsync();

        if (_whiteboardProjectService != null)
        {
            var affectedWhiteboardIds = await _context.Whiteboards
                .Where(w => w.ProjectId == projectId && w.OwnerId == targetUserId)
                .Select(w => w.Id)
                .ToListAsync();

            foreach (var whiteboardId in affectedWhiteboardIds)
            {
                await _whiteboardProjectService.TransferOwnershipIfNeededAsync(whiteboardId);
            }
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UpdateMemberRole(int projectId, string targetUserId, ProjectRole newRole, string updatedByUserId)
    {
        var updaterMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == updatedByUserId);

        if (updaterMembership == null || updaterMembership.Role != ProjectRole.Owner)
        {
            return ServiceResult.Failure("Only project owners can assign roles.");
        }

        if (targetUserId == updatedByUserId)
        {
            return ServiceResult.Failure("Cannot change your own role.");
        }

        var targetMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == targetUserId);

        if (targetMembership == null)
        {
            return ServiceResult.Failure("User is not a member of this project.");
        }

        if (newRole == ProjectRole.Owner)
        {
            return ServiceResult.Failure("Cannot assign the Owner role. There can only be one owner.");
        }

        targetMembership.Role = newRole;
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<ProjectMember>>> GetMembers(int projectId, string requestingUserId)
    {
        var isMember = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == requestingUserId);

        if (!isMember)
        {
            return ServiceResult<List<ProjectMember>>.Failure("You are not a member of this project.");
        }

        var members = await _context.ProjectMembers
            .Where(pm => pm.ProjectId == projectId)
            .Include(pm => pm.User)
            .OrderByDescending(pm => pm.Role)
            .ThenBy(pm => pm.User.DisplayName)
            .ToListAsync();

        return ServiceResult<List<ProjectMember>>.Success(members);
    }

    public async Task<List<string>> GetMemberUserIds(int projectId)
    {
        return await _context.ProjectMembers
            .Where(pm => pm.ProjectId == projectId)
            .Select(pm => pm.UserId)
            .ToListAsync();
    }
}
