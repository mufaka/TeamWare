using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class UserReviewEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;

    public UserReviewEntityTests()
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
            UserName = "test@test.com",
            Email = "test@test.com",
            DisplayName = "Test User"
        };
        _context.Users.Add(_user);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CanCreateUserReview()
    {
        var review = new UserReview
        {
            UserId = _user.Id,
            CompletedAt = DateTime.UtcNow,
            Notes = "Weekly review completed. Updated priorities."
        };

        _context.UserReviews.Add(review);
        await _context.SaveChangesAsync();

        var retrieved = await _context.UserReviews.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(_user.Id, retrieved.UserId);
        Assert.Equal("Weekly review completed. Updated priorities.", retrieved.Notes);
    }

    [Fact]
    public async Task CanCreateUserReview_WithoutNotes()
    {
        var review = new UserReview
        {
            UserId = _user.Id,
            CompletedAt = DateTime.UtcNow
        };

        _context.UserReviews.Add(review);
        await _context.SaveChangesAsync();

        var retrieved = await _context.UserReviews.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Notes);
    }

    [Fact]
    public async Task CompletedAt_IsPersisted()
    {
        var completedAt = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var review = new UserReview
        {
            UserId = _user.Id,
            CompletedAt = completedAt
        };

        _context.UserReviews.Add(review);
        await _context.SaveChangesAsync();

        var retrieved = await _context.UserReviews.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(completedAt, retrieved.CompletedAt);
    }

    [Fact]
    public async Task UserReview_HasAutoIncrementId()
    {
        var review1 = new UserReview { UserId = _user.Id, CompletedAt = DateTime.UtcNow };
        var review2 = new UserReview { UserId = _user.Id, CompletedAt = DateTime.UtcNow };

        _context.UserReviews.AddRange(review1, review2);
        await _context.SaveChangesAsync();

        Assert.True(review1.Id > 0);
        Assert.True(review2.Id > review1.Id);
    }

    [Fact]
    public async Task UserReview_NavigationProperty_LoadsUser()
    {
        var review = new UserReview
        {
            UserId = _user.Id,
            CompletedAt = DateTime.UtcNow,
            Notes = "Test review"
        };

        _context.UserReviews.Add(review);
        await _context.SaveChangesAsync();

        var retrieved = await _context.UserReviews
            .Include(r => r.User)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.User);
        Assert.Equal("Test User", retrieved.User.DisplayName);
    }

    [Fact]
    public async Task UserReview_CascadeDeletesWithUser()
    {
        var review = new UserReview
        {
            UserId = _user.Id,
            CompletedAt = DateTime.UtcNow
        };

        _context.UserReviews.Add(review);
        await _context.SaveChangesAsync();

        _context.Users.Remove(_user);
        await _context.SaveChangesAsync();

        var reviews = await _context.UserReviews.ToListAsync();
        Assert.Empty(reviews);
    }

    [Fact]
    public async Task UserReview_MultipleReviewsPerUser()
    {
        var reviews = new[]
        {
            new UserReview { UserId = _user.Id, CompletedAt = DateTime.UtcNow.AddDays(-14), Notes = "First review" },
            new UserReview { UserId = _user.Id, CompletedAt = DateTime.UtcNow.AddDays(-7), Notes = "Second review" },
            new UserReview { UserId = _user.Id, CompletedAt = DateTime.UtcNow, Notes = "Third review" }
        };

        _context.UserReviews.AddRange(reviews);
        await _context.SaveChangesAsync();

        var userReviews = await _context.UserReviews
            .Where(r => r.UserId == _user.Id)
            .OrderByDescending(r => r.CompletedAt)
            .ToListAsync();

        Assert.Equal(3, userReviews.Count);
        Assert.Equal("Third review", userReviews[0].Notes);
    }

    [Fact]
    public async Task Notes_MaxLength2000_IsEnforced()
    {
        var review = new UserReview
        {
            UserId = _user.Id,
            CompletedAt = DateTime.UtcNow,
            Notes = new string('a', 2000)
        };

        _context.UserReviews.Add(review);
        await _context.SaveChangesAsync();

        var retrieved = await _context.UserReviews.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(2000, retrieved.Notes!.Length);
    }

    [Fact]
    public async Task CanQueryByUserId_UsingIndex()
    {
        var user2 = new ApplicationUser
        {
            UserName = "user2@test.com",
            Email = "user2@test.com",
            DisplayName = "User Two"
        };
        _context.Users.Add(user2);
        await _context.SaveChangesAsync();

        _context.UserReviews.Add(new UserReview { UserId = _user.Id, CompletedAt = DateTime.UtcNow });
        _context.UserReviews.Add(new UserReview { UserId = user2.Id, CompletedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var user1Reviews = await _context.UserReviews.Where(r => r.UserId == _user.Id).ToListAsync();
        var user2Reviews = await _context.UserReviews.Where(r => r.UserId == user2.Id).ToListAsync();

        Assert.Single(user1Reviews);
        Assert.Single(user2Reviews);
    }

    [Fact]
    public async Task CanQueryByCompletedAt_UsingIndex()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);

        _context.UserReviews.Add(new UserReview { UserId = _user.Id, CompletedAt = DateTime.UtcNow.AddDays(-10) });
        _context.UserReviews.Add(new UserReview { UserId = _user.Id, CompletedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var recentReviews = await _context.UserReviews
            .Where(r => r.CompletedAt >= cutoff)
            .ToListAsync();

        Assert.Single(recentReviews);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
