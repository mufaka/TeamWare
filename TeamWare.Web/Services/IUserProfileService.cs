using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IUserProfileService
{
    Task<ServiceResult<ApplicationUser>> GetProfile(string userId);

    Task<ServiceResult> UpdateProfile(string userId, string displayName, string? avatarUrl);

    Task<ServiceResult> ChangePassword(string userId, string currentPassword, string newPassword);

    Task<ServiceResult> UpdateThemePreference(string userId, string theme);
}
