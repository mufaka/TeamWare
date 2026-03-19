using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Data;

public class ApplicationDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public ApplicationDbContextTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
    }

    [Fact]
    public void CanCreateDatabase()
    {
        var created = _context.Database.EnsureCreated();

        Assert.True(created);
    }

    [Fact]
    public void CanApplyMigrations()
    {
        _context.Database.Migrate();

        var appliedMigrations = _context.Database.GetAppliedMigrations();
        Assert.NotEmpty(appliedMigrations);
    }

    [Fact]
    public async Task CanAddAndRetrieveApplicationUser()
    {
        _context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            UserName = "testuser",
            Email = "test@example.com",
            DisplayName = "Test User",
            AvatarUrl = "https://example.com/avatar.png",
            ThemePreference = "dark"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "testuser");

        Assert.NotNull(retrieved);
        Assert.Equal("Test User", retrieved.DisplayName);
        Assert.Equal("https://example.com/avatar.png", retrieved.AvatarUrl);
        Assert.Equal("dark", retrieved.ThemePreference);
    }

    [Fact]
    public async Task ApplicationUser_DefaultThemePreference_IsSystem()
    {
        _context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            UserName = "defaultuser",
            Email = "default@example.com",
            DisplayName = "Default User"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "defaultuser");

        Assert.NotNull(retrieved);
        Assert.Equal("system", retrieved.ThemePreference);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
