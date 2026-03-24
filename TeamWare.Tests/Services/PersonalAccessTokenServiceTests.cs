using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class PersonalAccessTokenServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly PersonalAccessTokenService _service;
    private readonly ApplicationUser _user;
    private readonly ApplicationUser _adminUser;

    public PersonalAccessTokenServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _user = new ApplicationUser
        {
            UserName = "user@test.com",
            Email = "user@test.com",
            DisplayName = "Test User"
        };

        _adminUser = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            DisplayName = "Admin User"
        };

        _context.Users.Add(_user);
        _context.Users.Add(_adminUser);
        _context.SaveChanges();

        _service = new PersonalAccessTokenService(_context);
    }

    // --- CreateTokenAsync ---

    [Fact]
    public async Task CreateTokenAsync_ReturnsRawToken_WithTwPrefix()
    {
        var result = await _service.CreateTokenAsync(_user.Id, "My Token", null);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.StartsWith("tw_", result.Data);
    }

    [Fact]
    public async Task CreateTokenAsync_StoresTokenHash_NotRawToken()
    {
        var result = await _service.CreateTokenAsync(_user.Id, "My Token", null);
        var rawToken = result.Data!;

        var storedToken = await _context.PersonalAccessTokens.FirstAsync();

        Assert.NotEqual(rawToken, storedToken.TokenHash);
        Assert.Equal("My Token", storedToken.Name);
        Assert.Equal(_user.Id, storedToken.UserId);
    }

    [Fact]
    public async Task CreateTokenAsync_StoresTokenPrefix()
    {
        var result = await _service.CreateTokenAsync(_user.Id, "My Token", null);
        var rawToken = result.Data!;

        var storedToken = await _context.PersonalAccessTokens.FirstAsync();

        Assert.Equal(rawToken[..10], storedToken.TokenPrefix);
        Assert.StartsWith("tw_", storedToken.TokenPrefix);
    }

    [Fact]
    public async Task CreateTokenAsync_StoresExpirationDate()
    {
        var expiresAt = DateTime.UtcNow.AddDays(30);

        var result = await _service.CreateTokenAsync(_user.Id, "Expiring Token", expiresAt);

        Assert.True(result.Succeeded);
        var storedToken = await _context.PersonalAccessTokens.FirstAsync();
        Assert.NotNull(storedToken.ExpiresAt);
        Assert.True(Math.Abs((expiresAt - storedToken.ExpiresAt.Value).TotalSeconds) < 1);
    }

    [Fact]
    public async Task CreateTokenAsync_GeneratesUniqueTokens()
    {
        var result1 = await _service.CreateTokenAsync(_user.Id, "Token 1", null);
        var result2 = await _service.CreateTokenAsync(_user.Id, "Token 2", null);

        Assert.NotEqual(result1.Data, result2.Data);
    }

    [Fact]
    public async Task CreateTokenAsync_Fails_WhenMaxActiveTokensReached()
    {
        for (int i = 0; i < 10; i++)
        {
            var createResult = await _service.CreateTokenAsync(_user.Id, $"Token {i}", null);
            Assert.True(createResult.Succeeded);
        }

        var result = await _service.CreateTokenAsync(_user.Id, "Token 11", null);

        Assert.False(result.Succeeded);
        Assert.Contains("Maximum of 10", result.Errors[0]);
    }

    [Fact]
    public async Task CreateTokenAsync_AllowsNew_WhenExpiredTokensExist()
    {
        for (int i = 0; i < 10; i++)
        {
            _context.PersonalAccessTokens.Add(new PersonalAccessToken
            {
                Name = $"Expired Token {i}",
                TokenHash = $"expired_hash_{i}",
                TokenPrefix = $"tw_exp{i:D2}",
                UserId = _user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
        }

        await _context.SaveChangesAsync();

        var result = await _service.CreateTokenAsync(_user.Id, "New Token", null);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CreateTokenAsync_AllowsNew_WhenRevokedTokensExist()
    {
        for (int i = 0; i < 10; i++)
        {
            _context.PersonalAccessTokens.Add(new PersonalAccessToken
            {
                Name = $"Revoked Token {i}",
                TokenHash = $"revoked_hash_{i}",
                TokenPrefix = $"tw_rev{i:D2}",
                UserId = _user.Id,
                RevokedAt = DateTime.UtcNow.AddDays(-1)
            });
        }

        await _context.SaveChangesAsync();

        var result = await _service.CreateTokenAsync(_user.Id, "New Token", null);

        Assert.True(result.Succeeded);
    }

    // --- ValidateTokenAsync ---

    [Fact]
    public async Task ValidateTokenAsync_ReturnsUser_ForValidToken()
    {
        var createResult = await _service.CreateTokenAsync(_user.Id, "Valid Token", null);
        var rawToken = createResult.Data!;

        var result = await _service.ValidateTokenAsync(rawToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(_user.Id, result.Data.Id);
        Assert.Equal("Test User", result.Data.DisplayName);
    }

    [Fact]
    public async Task ValidateTokenAsync_UpdatesLastUsedAt()
    {
        var createResult = await _service.CreateTokenAsync(_user.Id, "Token", null);
        var rawToken = createResult.Data!;

        var beforeValidation = DateTime.UtcNow;
        await _service.ValidateTokenAsync(rawToken);

        var storedToken = await _context.PersonalAccessTokens.FirstAsync();
        Assert.NotNull(storedToken.LastUsedAt);
        Assert.True(storedToken.LastUsedAt >= beforeValidation);
    }

    [Fact]
    public async Task ValidateTokenAsync_Fails_ForInvalidToken()
    {
        var result = await _service.ValidateTokenAsync("tw_invalid_token_value");

        Assert.False(result.Succeeded);
        Assert.Contains("Invalid token", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateTokenAsync_Fails_ForExpiredToken()
    {
        var createResult = await _service.CreateTokenAsync(
            _user.Id, "Expired Token", DateTime.UtcNow.AddSeconds(-1));
        var rawToken = createResult.Data!;

        var result = await _service.ValidateTokenAsync(rawToken);

        Assert.False(result.Succeeded);
        Assert.Contains("expired", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateTokenAsync_Fails_ForRevokedToken()
    {
        var createResult = await _service.CreateTokenAsync(_user.Id, "Revoked Token", null);
        var rawToken = createResult.Data!;

        await _service.RevokeTokenAsync(
            (await _context.PersonalAccessTokens.FirstAsync()).Id,
            _user.Id,
            isAdmin: false);

        var result = await _service.ValidateTokenAsync(rawToken);

        Assert.False(result.Succeeded);
        Assert.Contains("revoked", result.Errors[0]);
    }

    // --- GetTokensForUserAsync ---

    [Fact]
    public async Task GetTokensForUserAsync_ReturnsOnlyActiveTokens()
    {
        await _service.CreateTokenAsync(_user.Id, "Active Token", null);

        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "Revoked Token",
            TokenHash = "revoked_hash",
            TokenPrefix = "tw_rev123",
            UserId = _user.Id,
            RevokedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        var result = await _service.GetTokensForUserAsync(_user.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Active Token", result.Data[0].Name);
    }

    [Fact]
    public async Task GetTokensForUserAsync_OrdersByCreatedAtDescending()
    {
        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "Older Token",
            TokenHash = "hash_older",
            TokenPrefix = "tw_old123",
            UserId = _user.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });

        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "Newer Token",
            TokenHash = "hash_newer",
            TokenPrefix = "tw_new123",
            UserId = _user.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        await _context.SaveChangesAsync();

        var result = await _service.GetTokensForUserAsync(_user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("Newer Token", result.Data[0].Name);
        Assert.Equal("Older Token", result.Data[1].Name);
    }

    [Fact]
    public async Task GetTokensForUserAsync_ReturnsOnlyOwnTokens()
    {
        await _service.CreateTokenAsync(_user.Id, "User Token", null);
        await _service.CreateTokenAsync(_adminUser.Id, "Admin Token", null);

        var result = await _service.GetTokensForUserAsync(_user.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("User Token", result.Data[0].Name);
    }

    // --- RevokeTokenAsync ---

    [Fact]
    public async Task RevokeTokenAsync_SetsRevokedAt()
    {
        await _service.CreateTokenAsync(_user.Id, "Token to Revoke", null);
        var token = await _context.PersonalAccessTokens.FirstAsync();

        var result = await _service.RevokeTokenAsync(token.Id, _user.Id, isAdmin: false);

        Assert.True(result.Succeeded);
        await _context.Entry(token).ReloadAsync();
        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public async Task RevokeTokenAsync_Fails_WhenTokenNotFound()
    {
        var result = await _service.RevokeTokenAsync(999, _user.Id, isAdmin: false);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors[0]);
    }

    [Fact]
    public async Task RevokeTokenAsync_Fails_WhenNotOwnerAndNotAdmin()
    {
        await _service.CreateTokenAsync(_user.Id, "Token", null);
        var token = await _context.PersonalAccessTokens.FirstAsync();

        var result = await _service.RevokeTokenAsync(token.Id, _adminUser.Id, isAdmin: false);

        Assert.False(result.Succeeded);
        Assert.Contains("permission", result.Errors[0]);
    }

    [Fact]
    public async Task RevokeTokenAsync_Succeeds_WhenAdmin()
    {
        await _service.CreateTokenAsync(_user.Id, "Token", null);
        var token = await _context.PersonalAccessTokens.FirstAsync();

        var result = await _service.RevokeTokenAsync(token.Id, _adminUser.Id, isAdmin: true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RevokeTokenAsync_Fails_WhenAlreadyRevoked()
    {
        await _service.CreateTokenAsync(_user.Id, "Token", null);
        var token = await _context.PersonalAccessTokens.FirstAsync();

        await _service.RevokeTokenAsync(token.Id, _user.Id, isAdmin: false);
        var result = await _service.RevokeTokenAsync(token.Id, _user.Id, isAdmin: false);

        Assert.False(result.Succeeded);
        Assert.Contains("already revoked", result.Errors[0]);
    }

    // --- RevokeAllTokensForUserAsync ---

    [Fact]
    public async Task RevokeAllTokensForUserAsync_RevokesAllActiveTokens()
    {
        await _service.CreateTokenAsync(_user.Id, "Token 1", null);
        await _service.CreateTokenAsync(_user.Id, "Token 2", null);
        await _service.CreateTokenAsync(_user.Id, "Token 3", null);

        var result = await _service.RevokeAllTokensForUserAsync(_user.Id);

        Assert.True(result.Succeeded);

        var tokens = await _context.PersonalAccessTokens
            .Where(t => t.UserId == _user.Id)
            .ToListAsync();

        Assert.All(tokens, t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task RevokeAllTokensForUserAsync_DoesNotAffectOtherUsers()
    {
        await _service.CreateTokenAsync(_user.Id, "User Token", null);
        await _service.CreateTokenAsync(_adminUser.Id, "Admin Token", null);

        await _service.RevokeAllTokensForUserAsync(_user.Id);

        var adminTokens = await _context.PersonalAccessTokens
            .Where(t => t.UserId == _adminUser.Id && t.RevokedAt == null)
            .ToListAsync();

        Assert.Single(adminTokens);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
