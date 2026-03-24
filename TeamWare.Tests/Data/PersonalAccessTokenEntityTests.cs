using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Data;

public class PersonalAccessTokenEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;

    public PersonalAccessTokenEntityTests()
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
            UserName = "testuser@test.com",
            Email = "testuser@test.com",
            DisplayName = "Test User"
        };

        _context.Users.Add(_user);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CanAddAndRetrievePersonalAccessToken()
    {
        var token = new PersonalAccessToken
        {
            Name = "My Token",
            TokenHash = "abc123hash",
            TokenPrefix = "tw_abc1",
            UserId = _user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        _context.PersonalAccessTokens.Add(token);
        await _context.SaveChangesAsync();

        var retrieved = await _context.PersonalAccessTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Name == "My Token");

        Assert.NotNull(retrieved);
        Assert.Equal("My Token", retrieved.Name);
        Assert.Equal("abc123hash", retrieved.TokenHash);
        Assert.Equal("tw_abc1", retrieved.TokenPrefix);
        Assert.Equal(_user.Id, retrieved.UserId);
        Assert.NotNull(retrieved.User);
        Assert.Equal("Test User", retrieved.User.DisplayName);
        Assert.NotNull(retrieved.ExpiresAt);
        Assert.Null(retrieved.LastUsedAt);
        Assert.Null(retrieved.RevokedAt);
    }

    [Fact]
    public async Task TokenHash_HasUniqueIndex()
    {
        var token1 = new PersonalAccessToken
        {
            Name = "Token 1",
            TokenHash = "duplicate_hash",
            TokenPrefix = "tw_dup1",
            UserId = _user.Id
        };

        var token2 = new PersonalAccessToken
        {
            Name = "Token 2",
            TokenHash = "duplicate_hash",
            TokenPrefix = "tw_dup2",
            UserId = _user.Id
        };

        _context.PersonalAccessTokens.Add(token1);
        await _context.SaveChangesAsync();

        _context.PersonalAccessTokens.Add(token2);
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task User_NavigationProperty_LoadsTokens()
    {
        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "Token A",
            TokenHash = "hash_a",
            TokenPrefix = "tw_aaa1",
            UserId = _user.Id
        });

        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "Token B",
            TokenHash = "hash_b",
            TokenPrefix = "tw_bbb1",
            UserId = _user.Id
        });

        await _context.SaveChangesAsync();

        var userWithTokens = await _context.Users
            .Include(u => u.PersonalAccessTokens)
            .FirstOrDefaultAsync(u => u.Id == _user.Id);

        Assert.NotNull(userWithTokens);
        Assert.Equal(2, userWithTokens.PersonalAccessTokens.Count);
    }

    [Fact]
    public async Task CascadeDelete_RemovesTokens_WhenUserDeleted()
    {
        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "Token To Delete",
            TokenHash = "hash_delete",
            TokenPrefix = "tw_del1",
            UserId = _user.Id
        });

        await _context.SaveChangesAsync();

        _context.Users.Remove(_user);
        await _context.SaveChangesAsync();

        var tokens = await _context.PersonalAccessTokens.ToListAsync();
        Assert.Empty(tokens);
    }

    [Fact]
    public async Task CanLookUpToken_ByTokenHash()
    {
        var expectedHash = "unique_lookup_hash";

        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "Lookup Token",
            TokenHash = expectedHash,
            TokenPrefix = "tw_lkp1",
            UserId = _user.Id
        });

        await _context.SaveChangesAsync();

        var found = await _context.PersonalAccessTokens
            .FirstOrDefaultAsync(t => t.TokenHash == expectedHash);

        Assert.NotNull(found);
        Assert.Equal("Lookup Token", found.Name);
    }

    [Fact]
    public async Task CanQueryTokens_ByUserId()
    {
        var otherUser = new ApplicationUser
        {
            UserName = "other@test.com",
            Email = "other@test.com",
            DisplayName = "Other User"
        };

        _context.Users.Add(otherUser);
        await _context.SaveChangesAsync();

        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "User1 Token",
            TokenHash = "hash_u1",
            TokenPrefix = "tw_u1t1",
            UserId = _user.Id
        });

        _context.PersonalAccessTokens.Add(new PersonalAccessToken
        {
            Name = "User2 Token",
            TokenHash = "hash_u2",
            TokenPrefix = "tw_u2t1",
            UserId = otherUser.Id
        });

        await _context.SaveChangesAsync();

        var userTokens = await _context.PersonalAccessTokens
            .Where(t => t.UserId == _user.Id)
            .ToListAsync();

        Assert.Single(userTokens);
        Assert.Equal("User1 Token", userTokens[0].Name);
    }

    [Fact]
    public async Task NullableFields_AreNullByDefault()
    {
        var token = new PersonalAccessToken
        {
            Name = "Minimal Token",
            TokenHash = "hash_minimal",
            TokenPrefix = "tw_min1",
            UserId = _user.Id
        };

        _context.PersonalAccessTokens.Add(token);
        await _context.SaveChangesAsync();

        var retrieved = await _context.PersonalAccessTokens.FirstAsync();

        Assert.Null(retrieved.ExpiresAt);
        Assert.Null(retrieved.LastUsedAt);
        Assert.Null(retrieved.RevokedAt);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
