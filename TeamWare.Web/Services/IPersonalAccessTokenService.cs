using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IPersonalAccessTokenService
{
    Task<ServiceResult<string>> CreateTokenAsync(string userId, string name, DateTime? expiresAt);

    Task<ServiceResult<ApplicationUser>> ValidateTokenAsync(string rawToken);

    Task<ServiceResult<List<PersonalAccessToken>>> GetTokensForUserAsync(string userId);

    Task<ServiceResult> RevokeTokenAsync(int tokenId, string userId, bool isAdmin);

    Task<ServiceResult> RevokeAllTokensForUserAsync(string userId);
}
