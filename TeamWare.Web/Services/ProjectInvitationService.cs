using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class ProjectInvitationService : IProjectInvitationService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;

    public ProjectInvitationService(ApplicationDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<ServiceResult<ProjectInvitation>> SendInvitation(int projectId, string invitedUserId, ProjectRole role, string invitedByUserId)
    {
        if (string.IsNullOrEmpty(invitedUserId))
        {
            return ServiceResult<ProjectInvitation>.Failure("Invited user ID is required.");
        }

        if (string.IsNullOrEmpty(invitedByUserId))
        {
            return ServiceResult<ProjectInvitation>.Failure("Inviter user ID is required.");
        }

        var project = await _context.Projects.FindAsync(projectId);
        if (project == null)
        {
            return ServiceResult<ProjectInvitation>.Failure("Project not found.");
        }

        var inviterMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == invitedByUserId);

        if (inviterMembership == null || (inviterMembership.Role != ProjectRole.Owner && inviterMembership.Role != ProjectRole.Admin))
        {
            return ServiceResult<ProjectInvitation>.Failure("Only project owners and admins can invite members.");
        }

        var invitedUser = await _context.Users.FindAsync(invitedUserId);
        if (invitedUser == null)
        {
            return ServiceResult<ProjectInvitation>.Failure("Invited user not found.");
        }

        var existingMembership = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == invitedUserId);

        if (existingMembership)
        {
            return ServiceResult<ProjectInvitation>.Failure("User is already a member of this project.");
        }

        var existingPendingInvitation = await _context.ProjectInvitations
            .AnyAsync(i => i.ProjectId == projectId && i.InvitedUserId == invitedUserId && i.Status == InvitationStatus.Pending);

        if (existingPendingInvitation)
        {
            return ServiceResult<ProjectInvitation>.Failure("User already has a pending invitation to this project.");
        }

        if (role == ProjectRole.Owner)
        {
            return ServiceResult<ProjectInvitation>.Failure("Cannot invite a user as Owner.");
        }

        var invitation = new ProjectInvitation
        {
            ProjectId = projectId,
            InvitedUserId = invitedUserId,
            InvitedByUserId = invitedByUserId,
            Status = invitedUser.IsAgent ? InvitationStatus.Accepted : InvitationStatus.Pending,
            Role = role,
            CreatedAt = DateTime.UtcNow,
            RespondedAt = invitedUser.IsAgent ? DateTime.UtcNow : null
        };

        _context.ProjectInvitations.Add(invitation);

        if (invitedUser.IsAgent)
        {
            var newMember = new ProjectMember
            {
                ProjectId = projectId,
                UserId = invitedUserId,
                Role = role,
                JoinedAt = DateTime.UtcNow
            };
            _context.ProjectMembers.Add(newMember);
        }

        await _context.SaveChangesAsync();

        if (!invitedUser.IsAgent)
        {
            var inviterUser = await _context.Users.FindAsync(invitedByUserId);
            var inviterName = inviterUser?.DisplayName ?? "Someone";

            await _notificationService.CreateNotification(
                invitedUserId,
                $"{inviterName} invited you to join project \"{project.Name}\"",
                NotificationType.ProjectInvitation,
                invitation.Id);
        }

        return ServiceResult<ProjectInvitation>.Success(invitation);
    }

    public async Task<ServiceResult<List<ProjectInvitation>>> SendBulkInvitations(int projectId, List<string> invitedUserIds, ProjectRole role, string invitedByUserId)
    {
        if (invitedUserIds == null || invitedUserIds.Count == 0)
        {
            return ServiceResult<List<ProjectInvitation>>.Failure("At least one user must be specified.");
        }

        var results = new List<ProjectInvitation>();
        var errors = new List<string>();

        foreach (var userId in invitedUserIds)
        {
            var result = await SendInvitation(projectId, userId, role, invitedByUserId);
            if (result.Succeeded && result.Data != null)
            {
                results.Add(result.Data);
            }
            else
            {
                errors.Add($"User {userId}: {result.Errors.FirstOrDefault()}");
            }
        }

        if (results.Count == 0 && errors.Count > 0)
        {
            return ServiceResult<List<ProjectInvitation>>.Failure(errors);
        }

        return ServiceResult<List<ProjectInvitation>>.Success(results);
    }

    public async Task<ServiceResult<ProjectMember>> AcceptInvitation(int invitationId, string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ServiceResult<ProjectMember>.Failure("User ID is required.");
        }

        var invitation = await _context.ProjectInvitations
            .Include(i => i.Project)
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation == null)
        {
            return ServiceResult<ProjectMember>.Failure("Invitation not found.");
        }

        if (invitation.InvitedUserId != userId)
        {
            return ServiceResult<ProjectMember>.Failure("You can only respond to your own invitations.");
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return ServiceResult<ProjectMember>.Failure("This invitation has already been responded to.");
        }

        var existingMembership = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == invitation.ProjectId && pm.UserId == userId);

        if (existingMembership)
        {
            invitation.Status = InvitationStatus.Accepted;
            invitation.RespondedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ServiceResult<ProjectMember>.Failure("You are already a member of this project.");
        }

        var newMember = new ProjectMember
        {
            ProjectId = invitation.ProjectId,
            UserId = userId,
            Role = invitation.Role,
            JoinedAt = DateTime.UtcNow
        };

        _context.ProjectMembers.Add(newMember);

        invitation.Status = InvitationStatus.Accepted;
        invitation.RespondedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult<ProjectMember>.Success(newMember);
    }

    public async Task<ServiceResult> DeclineInvitation(int invitationId, string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ServiceResult.Failure("User ID is required.");
        }

        var invitation = await _context.ProjectInvitations.FindAsync(invitationId);

        if (invitation == null)
        {
            return ServiceResult.Failure("Invitation not found.");
        }

        if (invitation.InvitedUserId != userId)
        {
            return ServiceResult.Failure("You can only respond to your own invitations.");
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return ServiceResult.Failure("This invitation has already been responded to.");
        }

        invitation.Status = InvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<ProjectInvitation>>> GetPendingInvitationsForProject(int projectId, string requestingUserId)
    {
        if (string.IsNullOrEmpty(requestingUserId))
        {
            return ServiceResult<List<ProjectInvitation>>.Failure("Requesting user ID is required.");
        }

        var requesterMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == requestingUserId);

        if (requesterMembership == null || (requesterMembership.Role != ProjectRole.Owner && requesterMembership.Role != ProjectRole.Admin))
        {
            return ServiceResult<List<ProjectInvitation>>.Failure("Only project owners and admins can view pending invitations.");
        }

        var invitations = await _context.ProjectInvitations
            .Where(i => i.ProjectId == projectId && i.Status == InvitationStatus.Pending)
            .Include(i => i.InvitedUser)
            .Include(i => i.InvitedByUser)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return ServiceResult<List<ProjectInvitation>>.Success(invitations);
    }

    public async Task<ServiceResult<List<ProjectInvitation>>> GetPendingInvitationsForUser(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ServiceResult<List<ProjectInvitation>>.Failure("User ID is required.");
        }

        var invitations = await _context.ProjectInvitations
            .Where(i => i.InvitedUserId == userId && i.Status == InvitationStatus.Pending)
            .Include(i => i.Project)
            .Include(i => i.InvitedByUser)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return ServiceResult<List<ProjectInvitation>>.Success(invitations);
    }

    public async Task<ServiceResult> CancelInvitation(int invitationId, string cancelledByUserId)
    {
        if (string.IsNullOrEmpty(cancelledByUserId))
        {
            return ServiceResult.Failure("User ID is required.");
        }

        var invitation = await _context.ProjectInvitations.FindAsync(invitationId);

        if (invitation == null)
        {
            return ServiceResult.Failure("Invitation not found.");
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return ServiceResult.Failure("Only pending invitations can be cancelled.");
        }

        var cancellerMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == invitation.ProjectId && pm.UserId == cancelledByUserId);

        if (cancellerMembership == null || (cancellerMembership.Role != ProjectRole.Owner && cancellerMembership.Role != ProjectRole.Admin))
        {
            return ServiceResult.Failure("Only project owners and admins can cancel invitations.");
        }

        invitation.Status = InvitationStatus.Cancelled;
        invitation.RespondedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<ProjectInvitation>> ResendInvitation(int invitationId, string resentByUserId)
    {
        if (string.IsNullOrEmpty(resentByUserId))
        {
            return ServiceResult<ProjectInvitation>.Failure("User ID is required.");
        }

        var invitation = await _context.ProjectInvitations
            .Include(i => i.Project)
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation == null)
        {
            return ServiceResult<ProjectInvitation>.Failure("Invitation not found.");
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return ServiceResult<ProjectInvitation>.Failure("Only pending invitations can be resent.");
        }

        var resenderMembership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == invitation.ProjectId && pm.UserId == resentByUserId);

        if (resenderMembership == null || (resenderMembership.Role != ProjectRole.Owner && resenderMembership.Role != ProjectRole.Admin))
        {
            return ServiceResult<ProjectInvitation>.Failure("Only project owners and admins can resend invitations.");
        }

        var invitedUser = await _context.Users.FindAsync(invitation.InvitedUserId);
        if (invitedUser == null)
        {
            return ServiceResult<ProjectInvitation>.Failure("Invited user not found.");
        }

        if (invitedUser.IsAgent)
        {
            invitation.Status = InvitationStatus.Accepted;
            invitation.RespondedAt = DateTime.UtcNow;

            var existingMembership = await _context.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == invitation.ProjectId && pm.UserId == invitation.InvitedUserId);

            if (!existingMembership)
            {
                var newMember = new ProjectMember
                {
                    ProjectId = invitation.ProjectId,
                    UserId = invitation.InvitedUserId,
                    Role = invitation.Role,
                    JoinedAt = DateTime.UtcNow
                };
                _context.ProjectMembers.Add(newMember);
            }
        }
        else
        {
            var resenderUser = await _context.Users.FindAsync(resentByUserId);
            var resenderName = resenderUser?.DisplayName ?? "Someone";

            await _notificationService.CreateNotification(
                invitation.InvitedUserId,
                $"{resenderName} reminded you about the invitation to join project \"{invitation.Project.Name}\"",
                NotificationType.ProjectInvitation,
                invitation.Id);
        }

        await _context.SaveChangesAsync();

        return ServiceResult<ProjectInvitation>.Success(invitation);
    }
}
