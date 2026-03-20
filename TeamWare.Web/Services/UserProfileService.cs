using Microsoft.AspNetCore.Identity;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class UserProfileService : IUserProfileService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserProfileService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ServiceResult<ApplicationUser>> GetProfile(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<ApplicationUser>.Failure("User ID is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult<ApplicationUser>.Failure("User not found.");
        }

        return ServiceResult<ApplicationUser>.Success(user);
    }

    public async Task<ServiceResult> UpdateProfile(string userId, string displayName, string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult.Failure("User ID is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return ServiceResult.Failure("Display name is required.");
        }

        if (displayName.Length > 100)
        {
            return ServiceResult.Failure("Display name must not exceed 100 characters.");
        }

        if (avatarUrl != null && avatarUrl.Length > 500)
        {
            return ServiceResult.Failure("Avatar URL must not exceed 500 characters.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        user.DisplayName = displayName;
        user.AvatarUrl = avatarUrl;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(e => e.Description));
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ChangePassword(string userId, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult.Failure("User ID is required.");
        }

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            return ServiceResult.Failure("Current password is required.");
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return ServiceResult.Failure("New password is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(e => e.Description));
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UpdateThemePreference(string userId, string theme)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult.Failure("User ID is required.");
        }

        var validThemes = new[] { "light", "dark", "system" };
        if (!validThemes.Contains(theme, StringComparer.OrdinalIgnoreCase))
        {
            return ServiceResult.Failure("Theme must be 'light', 'dark', or 'system'.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure("User not found.");
        }

        user.ThemePreference = theme.ToLowerInvariant();

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(e => e.Description));
        }

        return ServiceResult.Success();
    }
}
