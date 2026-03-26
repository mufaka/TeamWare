using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public class AdminService : IAdminService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAdminActivityLogService _activityLog;
    private readonly IPersonalAccessTokenService _tokenService;

    public AdminService(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IAdminActivityLogService activityLog,
        IPersonalAccessTokenService tokenService)
    {
        _context = context;
        _userManager = userManager;
        _activityLog = activityLog;
        _tokenService = tokenService;
    }

    public async Task<ServiceResult<PagedResult<ApplicationUser>>> GetAllUsers(string? searchTerm, int page, int pageSize)
    {
        if (page < 1)
        {
            return ServiceResult<PagedResult<ApplicationUser>>.Failure("Page must be at least 1.");
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return ServiceResult<PagedResult<ApplicationUser>>.Failure("Page size must be between 1 and 100.");
        }

        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(term) ||
                (u.Email != null && u.Email.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = new PagedResult<ApplicationUser>(users, totalCount, page, pageSize);
        return ServiceResult<PagedResult<ApplicationUser>>.Success(result);
    }

    public async Task<ServiceResult> LockUser(string targetUserId, string adminUserId)
    {
        var targetUser = await _userManager.FindByIdAsync(targetUserId);
        if (targetUser == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        if (targetUserId == adminUserId)
        {
            return ServiceResult.Failure("You cannot lock your own account.");
        }

        await _userManager.SetLockoutEnabledAsync(targetUser, true);
        await _userManager.SetLockoutEndDateAsync(targetUser, DateTimeOffset.MaxValue);

        await _activityLog.LogAction(adminUserId, "LockAccount", targetUserId,
            details: $"Locked account for {targetUser.Email}");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UnlockUser(string targetUserId, string adminUserId)
    {
        var targetUser = await _userManager.FindByIdAsync(targetUserId);
        if (targetUser == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        await _userManager.SetLockoutEndDateAsync(targetUser, null);

        await _activityLog.LogAction(adminUserId, "UnlockAccount", targetUserId,
            details: $"Unlocked account for {targetUser.Email}");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ResetPassword(string targetUserId, string newPassword, string adminUserId)
    {
        var targetUser = await _userManager.FindByIdAsync(targetUserId);
        if (targetUser == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(targetUser);
        var result = await _userManager.ResetPasswordAsync(targetUser, token, newPassword);

        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(e => e.Description));
        }

        await _activityLog.LogAction(adminUserId, "ResetPassword", targetUserId,
            details: $"Reset password for {targetUser.Email}");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> PromoteToAdmin(string targetUserId, string adminUserId)
    {
        var targetUser = await _userManager.FindByIdAsync(targetUserId);
        if (targetUser == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        if (await _userManager.IsInRoleAsync(targetUser, SeedData.AdminRoleName))
        {
            return ServiceResult.Failure("User is already an admin.");
        }

        var result = await _userManager.AddToRoleAsync(targetUser, SeedData.AdminRoleName);
        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(e => e.Description));
        }

        await _activityLog.LogAction(adminUserId, "PromoteToAdmin", targetUserId,
            details: $"Promoted {targetUser.Email} to Admin role");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DemoteToUser(string targetUserId, string adminUserId)
    {
        var targetUser = await _userManager.FindByIdAsync(targetUserId);
        if (targetUser == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        if (targetUserId == adminUserId)
        {
            return ServiceResult.Failure("You cannot demote your own account.");
        }

        if (!await _userManager.IsInRoleAsync(targetUser, SeedData.AdminRoleName))
        {
            return ServiceResult.Failure("User is not an admin.");
        }

        var result = await _userManager.RemoveFromRoleAsync(targetUser, SeedData.AdminRoleName);
        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(e => e.Description));
        }

        await _activityLog.LogAction(adminUserId, "DemoteToUser", targetUserId,
            details: $"Demoted {targetUser.Email} from Admin role");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<SystemStatistics>> GetSystemStatistics()
    {
        var stats = new SystemStatistics
        {
            TotalUsers = await _context.Users.CountAsync(),
            TotalProjects = await _context.Projects.CountAsync(),
            TotalTasks = await _context.TaskItems.CountAsync()
        };

        return ServiceResult<SystemStatistics>.Success(stats);
    }

    public async Task<ServiceResult<(ApplicationUser User, string RawToken)>> CreateAgentUser(string displayName, string? agentDescription, string adminUserId)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return ServiceResult<(ApplicationUser, string)>.Failure("Display name is required.");
        }

        var username = "agent-" + displayName.Trim().ToLowerInvariant().Replace(" ", "-");
        var email = $"{username}@agent.local";

        var passwordBytes = new byte[32];
        RandomNumberGenerator.Fill(passwordBytes);
        var password = Convert.ToBase64String(passwordBytes) + "A1!";

        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            DisplayName = displayName.Trim(),
            IsAgent = true,
            IsAgentActive = true,
            AgentDescription = agentDescription?.Trim()
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return ServiceResult<(ApplicationUser, string)>.Failure(
                createResult.Errors.Select(e => e.Description));
        }

        await _userManager.AddToRoleAsync(user, SeedData.UserRoleName);

        var tokenResult = await _tokenService.CreateTokenAsync(user.Id, "Default Agent Token", null);
        if (!tokenResult.Succeeded)
        {
            return ServiceResult<(ApplicationUser, string)>.Failure(tokenResult.Errors);
        }

        await _activityLog.LogAction(adminUserId, "CreateAgentUser", user.Id,
            details: $"Created agent user '{displayName}'");

        return ServiceResult<(ApplicationUser, string)>.Success((user, tokenResult.Data!));
    }

    public async Task<ServiceResult> UpdateAgentUser(string userId, string displayName, string? agentDescription, string adminUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        if (!user.IsAgent)
        {
            return ServiceResult.Failure("User is not an agent.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return ServiceResult.Failure("Display name is required.");
        }

        user.DisplayName = displayName.Trim();
        user.AgentDescription = agentDescription?.Trim();

        await _userManager.UpdateAsync(user);

        await _activityLog.LogAction(adminUserId, "UpdateAgentUser", userId,
            details: $"Updated agent user '{displayName}'");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetAgentActive(string userId, bool isActive, string adminUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        if (!user.IsAgent)
        {
            return ServiceResult.Failure("User is not an agent.");
        }

        user.IsAgentActive = isActive;
        await _userManager.UpdateAsync(user);

        var action = isActive ? "ResumeAgent" : "PauseAgent";
        await _activityLog.LogAction(adminUserId, action, userId,
            details: $"{action} for agent '{user.DisplayName}'");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<AgentUserSummary>>> GetAgentUsers()
    {
        var agents = await _context.Users
            .Where(u => u.IsAgent)
            .Select(u => new AgentUserSummary
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                AgentDescription = u.AgentDescription,
                IsAgentActive = u.IsAgentActive,
                LastActiveAt = u.LastActiveAt,
                AssignedTaskCount = _context.TaskAssignments
                    .Count(ta => ta.UserId == u.Id && ta.TaskItem.Status != TaskItemStatus.Done)
            })
            .OrderBy(a => a.DisplayName)
            .ToListAsync();

        return ServiceResult<List<AgentUserSummary>>.Success(agents);
    }

    public async Task<ServiceResult> DeleteAgentUser(string userId, string adminUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        if (!user.IsAgent)
        {
            return ServiceResult.Failure("User is not an agent.");
        }

        var displayName = user.DisplayName;

        await _activityLog.LogAction(adminUserId, "DeleteAgentUser", userId,
            details: $"Deleted agent user '{displayName}'");

        await _tokenService.RevokeAllTokensForUserAsync(userId);

        var tokens = await _context.PersonalAccessTokens
            .Where(t => t.UserId == userId)
            .ToListAsync();
        _context.PersonalAccessTokens.RemoveRange(tokens);

        var activityLogs = await _context.AdminActivityLogs
            .Where(l => l.TargetUserId == userId)
            .ToListAsync();
        foreach (var log in activityLogs)
        {
            log.TargetUserId = null;
        }

        await _context.SaveChangesAsync();

        var deleteResult = await _userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            return ServiceResult.Failure(deleteResult.Errors.Select(e => e.Description));
        }

        return ServiceResult.Success();
    }
}
