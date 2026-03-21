using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class AdminService : IAdminService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAdminActivityLogService _activityLog;

    public AdminService(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IAdminActivityLogService activityLog)
    {
        _context = context;
        _userManager = userManager;
        _activityLog = activityLog;
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
}
