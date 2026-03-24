using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class PersonalAccessTokenService : IPersonalAccessTokenService
{
    private const int MaxActiveTokensPerUser = 10;
    private const string TokenPrefixValue = "tw_";

    private readonly ApplicationDbContext _context;

    public PersonalAccessTokenService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<string>> CreateTokenAsync(string userId, string name, DateTime? expiresAt)
    {
        var activeTokenCount = await _context.PersonalAccessTokens
            .CountAsync(t => t.UserId == userId
                && t.RevokedAt == null
                && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow));

        if (activeTokenCount >= MaxActiveTokensPerUser)
        {
            return ServiceResult<string>.Failure($"Maximum of {MaxActiveTokensPerUser} active tokens per user reached.");
        }

        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var rawToken = TokenPrefixValue + Convert.ToBase64String(randomBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var tokenHash = ComputeSha256Hash(rawToken);
        var tokenPrefix = rawToken[..Math.Min(rawToken.Length, 10)];

        var token = new PersonalAccessToken
        {
            Name = name,
            TokenHash = tokenHash,
            TokenPrefix = tokenPrefix,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        _context.PersonalAccessTokens.Add(token);
        await _context.SaveChangesAsync();

        return ServiceResult<string>.Success(rawToken);
    }

    public async Task<ServiceResult<ApplicationUser>> ValidateTokenAsync(string rawToken)
    {
        var tokenHash = ComputeSha256Hash(rawToken);

        var token = await _context.PersonalAccessTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (token == null)
        {
            return ServiceResult<ApplicationUser>.Failure("Invalid token.");
        }

        if (token.RevokedAt != null)
        {
            return ServiceResult<ApplicationUser>.Failure("Token has been revoked.");
        }

        if (token.ExpiresAt != null && token.ExpiresAt <= DateTime.UtcNow)
        {
            return ServiceResult<ApplicationUser>.Failure("Token has expired.");
        }

        token.LastUsedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult<ApplicationUser>.Success(token.User);
    }

    public async Task<ServiceResult<List<PersonalAccessToken>>> GetTokensForUserAsync(string userId)
    {
        var tokens = await _context.PersonalAccessTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return ServiceResult<List<PersonalAccessToken>>.Success(tokens);
    }

    public async Task<ServiceResult> RevokeTokenAsync(int tokenId, string userId, bool isAdmin)
    {
        var token = await _context.PersonalAccessTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId);

        if (token == null)
        {
            return ServiceResult.Failure("Token not found.");
        }

        if (token.UserId != userId && !isAdmin)
        {
            return ServiceResult.Failure("You do not have permission to revoke this token.");
        }

        if (token.RevokedAt != null)
        {
            return ServiceResult.Failure("Token is already revoked.");
        }

        token.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RevokeAllTokensForUserAsync(string userId)
    {
        var activeTokens = await _context.PersonalAccessTokens
            .Where(t => t.UserId == userId
                && t.RevokedAt == null)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    private static string ComputeSha256Hash(string rawInput)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawInput));
        return Convert.ToHexStringLower(hashBytes);
    }
}
