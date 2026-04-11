using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class WhiteboardPresenceTrackerTests
{
    private readonly WhiteboardPresenceTracker _tracker = new();

    [Fact]
    public async Task AddAndRemoveConnections_UpdatesActiveUserList()
    {
        await _tracker.AddConnectionAsync(1, "user-1", "conn-1");

        var activeUsers = await _tracker.GetActiveUsersAsync(1);
        Assert.Single(activeUsers);
        Assert.Contains("user-1", activeUsers);

        var removal = await _tracker.RemoveConnectionAsync("conn-1");

        Assert.NotNull(removal);
        Assert.Equal(1, removal.Value.WhiteboardId);
        Assert.Equal("user-1", removal.Value.UserId);
        Assert.Empty(await _tracker.GetActiveUsersAsync(1));
    }

    [Fact]
    public async Task MultipleConnections_FromSameUser_CountAsOneActiveUser()
    {
        await _tracker.AddConnectionAsync(1, "user-1", "conn-1");
        await _tracker.AddConnectionAsync(1, "user-1", "conn-2");

        var activeUsers = await _tracker.GetActiveUsersAsync(1);

        Assert.Single(activeUsers);
        Assert.True(await _tracker.IsUserActiveAsync(1, "user-1"));
    }

    [Fact]
    public async Task RemovingLastConnection_RemovesUserFromActiveList()
    {
        await _tracker.AddConnectionAsync(1, "user-1", "conn-1");
        await _tracker.AddConnectionAsync(1, "user-1", "conn-2");

        var firstRemoval = await _tracker.RemoveConnectionAsync("conn-1");
        var stillActiveAfterFirstRemoval = await _tracker.IsUserActiveAsync(1, "user-1");
        var secondRemoval = await _tracker.RemoveConnectionAsync("conn-2");
        var stillActiveAfterSecondRemoval = await _tracker.IsUserActiveAsync(1, "user-1");

        Assert.Null(firstRemoval);
        Assert.True(stillActiveAfterFirstRemoval);
        Assert.NotNull(secondRemoval);
        Assert.False(stillActiveAfterSecondRemoval);
    }

    [Fact]
    public async Task GetActiveUsersAsync_ReturnsCorrectList()
    {
        await _tracker.AddConnectionAsync(1, "user-2", "conn-2");
        await _tracker.AddConnectionAsync(1, "user-1", "conn-1");
        await _tracker.AddConnectionAsync(2, "user-3", "conn-3");

        var activeUsers = await _tracker.GetActiveUsersAsync(1);

        Assert.Equal(2, activeUsers.Count);
        Assert.Contains("user-1", activeUsers);
        Assert.Contains("user-2", activeUsers);
        Assert.DoesNotContain("user-3", activeUsers);
    }
}
