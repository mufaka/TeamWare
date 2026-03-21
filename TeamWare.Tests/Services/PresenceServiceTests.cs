using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class PresenceServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly PresenceService _service;

    public PresenceServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new PresenceService(_context);

        // Reset static state between test classes
        PresenceService.ResetState();
    }

    private ApplicationUser CreateUser(string email, string displayName)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        return user;
    }

    // --- TrackUserConnection ---

    [Fact]
    public void TrackUserConnection_AddsUser_ToOnlineUsers()
    {
        _service.TrackUserConnection("user1", "conn1");

        Assert.True(_service.IsUserOnline("user1"));
    }

    [Fact]
    public void TrackUserConnection_MultipleConnections_UserStillOnline()
    {
        _service.TrackUserConnection("user1", "conn1");
        _service.TrackUserConnection("user1", "conn2");

        Assert.True(_service.IsUserOnline("user1"));
    }

    [Fact]
    public void TrackUserConnection_MultipleUsers_AllOnline()
    {
        _service.TrackUserConnection("user1", "conn1");
        _service.TrackUserConnection("user2", "conn2");

        var onlineUsers = _service.GetOnlineUsers();
        Assert.Contains("user1", onlineUsers);
        Assert.Contains("user2", onlineUsers);
    }

    // --- TrackUserDisconnection ---

    [Fact]
    public void TrackUserDisconnection_RemovesUser_WhenLastConnectionClosed()
    {
        _service.TrackUserConnection("user1", "conn1");
        _service.TrackUserDisconnection("user1", "conn1");

        Assert.False(_service.IsUserOnline("user1"));
    }

    [Fact]
    public void TrackUserDisconnection_UserStillOnline_WhenOtherConnectionsExist()
    {
        _service.TrackUserConnection("user1", "conn1");
        _service.TrackUserConnection("user1", "conn2");
        _service.TrackUserDisconnection("user1", "conn1");

        Assert.True(_service.IsUserOnline("user1"));
    }

    [Fact]
    public void TrackUserDisconnection_NoError_WhenUserNotTracked()
    {
        // Should not throw
        _service.TrackUserDisconnection("nonexistent", "conn1");

        Assert.False(_service.IsUserOnline("nonexistent"));
    }

    // --- GetOnlineUsers ---

    [Fact]
    public void GetOnlineUsers_ReturnsEmpty_WhenNoUsersConnected()
    {
        var onlineUsers = _service.GetOnlineUsers();

        Assert.Empty(onlineUsers);
    }

    [Fact]
    public void GetOnlineUsers_ReturnsCorrectCount()
    {
        _service.TrackUserConnection("user1", "conn1");
        _service.TrackUserConnection("user2", "conn2");
        _service.TrackUserConnection("user3", "conn3");

        var onlineUsers = _service.GetOnlineUsers();

        Assert.Equal(3, onlineUsers.Count);
    }

    [Fact]
    public void GetOnlineUsers_DoesNotIncludeDisconnectedUsers()
    {
        _service.TrackUserConnection("user1", "conn1");
        _service.TrackUserConnection("user2", "conn2");
        _service.TrackUserDisconnection("user1", "conn1");

        var onlineUsers = _service.GetOnlineUsers();

        Assert.DoesNotContain("user1", onlineUsers);
        Assert.Contains("user2", onlineUsers);
    }

    // --- IsUserOnline ---

    [Fact]
    public void IsUserOnline_ReturnsFalse_WhenUserNeverConnected()
    {
        Assert.False(_service.IsUserOnline("unknown"));
    }

    [Fact]
    public void IsUserOnline_ReturnsTrue_WhenUserConnected()
    {
        _service.TrackUserConnection("user1", "conn1");

        Assert.True(_service.IsUserOnline("user1"));
    }

    // --- UpdateLastActive ---

    [Fact]
    public async Task UpdateLastActive_SetsTimestamp_ForExistingUser()
    {
        var user = CreateUser("test@test.com", "Test User");
        Assert.Null(user.LastActiveAt);

        await _service.UpdateLastActive(user.Id);

        var updated = await _context.Users.FirstAsync(u => u.Id == user.Id);
        Assert.NotNull(updated.LastActiveAt);
        Assert.True(updated.LastActiveAt!.Value <= DateTime.UtcNow);
        Assert.True(updated.LastActiveAt!.Value > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task UpdateLastActive_NoError_WhenUserNotFound()
    {
        // Should not throw
        await _service.UpdateLastActive("nonexistent-id");
    }

    [Fact]
    public async Task UpdateLastActive_UpdatesExistingTimestamp()
    {
        var user = CreateUser("test2@test.com", "Test User 2");

        await _service.UpdateLastActive(user.Id);
        var firstUpdate = (await _context.Users.FirstAsync(u => u.Id == user.Id)).LastActiveAt;

        await Task.Delay(10); // Small delay to ensure different timestamp

        await _service.UpdateLastActive(user.Id);
        var secondUpdate = (await _context.Users.FirstAsync(u => u.Id == user.Id)).LastActiveAt;

        Assert.NotNull(firstUpdate);
        Assert.NotNull(secondUpdate);
        Assert.True(secondUpdate >= firstUpdate);
    }

    public void Dispose()
    {
        PresenceService.ResetState();
        _context.Dispose();
        _connection.Dispose();
    }
}
