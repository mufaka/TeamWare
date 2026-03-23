using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class GlobalConfigurationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly GlobalConfigurationService _service;
    private readonly ApplicationUser _adminUser;

    public GlobalConfigurationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _adminUser = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            DisplayName = "Admin User"
        };

        _context.Users.Add(_adminUser);
        _context.SaveChanges();

        var activityLogService = new AdminActivityLogService(_context);
        _service = new GlobalConfigurationService(_context, activityLogService);
    }

    private async Task SeedConfigAsync(string key = "TEST_KEY", string value = "test_value", string? description = null)
    {
        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = key,
            Value = value,
            Description = description
        });
        await _context.SaveChangesAsync();
    }

    // --- GetAllAsync ---

    [Fact]
    public async Task GetAllAsync_ReturnsAllConfigurations()
    {
        await SeedConfigAsync("KEY_A", "value_a");
        await SeedConfigAsync("KEY_B", "value_b");

        var result = await _service.GetAllAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOrderedByKey()
    {
        await SeedConfigAsync("ZEBRA_KEY", "z");
        await SeedConfigAsync("ALPHA_KEY", "a");

        var result = await _service.GetAllAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("ALPHA_KEY", result.Data![0].Key);
        Assert.Equal("ZEBRA_KEY", result.Data[1].Key);
    }

    [Fact]
    public async Task GetAllAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _service.GetAllAsync();

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    // --- GetByKeyAsync ---

    [Fact]
    public async Task GetByKeyAsync_ReturnsConfiguration()
    {
        await SeedConfigAsync("EXISTING_KEY", "the_value", "A description");

        var result = await _service.GetByKeyAsync("EXISTING_KEY");

        Assert.True(result.Succeeded);
        Assert.Equal("EXISTING_KEY", result.Data!.Key);
        Assert.Equal("the_value", result.Data.Value);
        Assert.Equal("A description", result.Data.Description);
    }

    [Fact]
    public async Task GetByKeyAsync_NotFound_ReturnsFailure()
    {
        var result = await _service.GetByKeyAsync("NONEXISTENT");

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors[0]);
    }

    // --- UpdateAsync ---

    [Fact]
    public async Task UpdateAsync_UpdatesValue()
    {
        await SeedConfigAsync("UPDATE_KEY", "old_value");

        var result = await _service.UpdateAsync("UPDATE_KEY", "new_value", _adminUser.Id);

        Assert.True(result.Succeeded);

        var config = await _context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == "UPDATE_KEY");

        Assert.NotNull(config);
        Assert.Equal("new_value", config.Value);
        Assert.Equal(_adminUser.Id, config.UpdatedByUserId);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesTimestamp()
    {
        await SeedConfigAsync("TIMESTAMP_KEY", "value");

        var before = DateTime.UtcNow;
        await _service.UpdateAsync("TIMESTAMP_KEY", "new_value", _adminUser.Id);

        var config = await _context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == "TIMESTAMP_KEY");

        Assert.NotNull(config);
        Assert.True(config.UpdatedAt >= before);
    }

    [Fact]
    public async Task UpdateAsync_LogsActivity()
    {
        await SeedConfigAsync("LOG_KEY", "old");

        await _service.UpdateAsync("LOG_KEY", "new", _adminUser.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "UpdateConfiguration");

        Assert.NotNull(log);
        Assert.Equal(_adminUser.Id, log.AdminUserId);
        Assert.Contains("LOG_KEY", log.Details!);
        Assert.Contains("old", log.Details!);
        Assert.Contains("new", log.Details!);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsFailure()
    {
        var result = await _service.UpdateAsync("NONEXISTENT", "value", _adminUser.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors[0]);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
