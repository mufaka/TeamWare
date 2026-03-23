using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class GlobalConfigurationEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public GlobalConfigurationEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task CanCreateGlobalConfiguration()
    {
        var config = new GlobalConfiguration
        {
            Key = "TEST_KEY",
            Value = "test_value",
            Description = "A test configuration entry"
        };

        _context.GlobalConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var retrieved = await _context.GlobalConfigurations.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal("TEST_KEY", retrieved.Key);
        Assert.Equal("test_value", retrieved.Value);
        Assert.Equal("A test configuration entry", retrieved.Description);
    }

    [Fact]
    public async Task GlobalConfiguration_HasTimestamp()
    {
        var before = DateTime.UtcNow;

        var config = new GlobalConfiguration
        {
            Key = "TIMESTAMP_TEST",
            Value = "value"
        };

        _context.GlobalConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var retrieved = await _context.GlobalConfigurations.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.UpdatedAt >= before);
        Assert.True(retrieved.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task GlobalConfiguration_KeyIsUnique()
    {
        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = "UNIQUE_KEY",
            Value = "value1"
        });
        await _context.SaveChangesAsync();

        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = "UNIQUE_KEY",
            Value = "value2"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task GlobalConfiguration_DescriptionIsOptional()
    {
        var config = new GlobalConfiguration
        {
            Key = "NO_DESC",
            Value = "value"
        };

        _context.GlobalConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var retrieved = await _context.GlobalConfigurations.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Description);
    }

    [Fact]
    public async Task GlobalConfiguration_UpdatedByUserIsOptional()
    {
        var config = new GlobalConfiguration
        {
            Key = "NO_USER",
            Value = "value"
        };

        _context.GlobalConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var retrieved = await _context.GlobalConfigurations.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.UpdatedByUserId);
    }

    [Fact]
    public async Task GlobalConfiguration_CanTrackUpdatingUser()
    {
        var user = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            DisplayName = "Admin"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new GlobalConfiguration
        {
            Key = "TRACKED_KEY",
            Value = "value",
            UpdatedByUserId = user.Id
        };

        _context.GlobalConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var retrieved = await _context.GlobalConfigurations
            .Include(gc => gc.UpdatedByUser)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.Equal(user.Id, retrieved.UpdatedByUserId);
        Assert.NotNull(retrieved.UpdatedByUser);
        Assert.Equal("Admin", retrieved.UpdatedByUser.DisplayName);
    }

    [Fact]
    public async Task GlobalConfiguration_CanUpdateValue()
    {
        var config = new GlobalConfiguration
        {
            Key = "MUTABLE_KEY",
            Value = "original"
        };

        _context.GlobalConfigurations.Add(config);
        await _context.SaveChangesAsync();

        config.Value = "updated";
        config.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var retrieved = await _context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == "MUTABLE_KEY");

        Assert.NotNull(retrieved);
        Assert.Equal("updated", retrieved.Value);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
