using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class WhiteboardChatServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly WhiteboardChatService _service;

    public WhiteboardChatServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new WhiteboardChatService(_context);
    }

    [Fact]
    public async Task SendMessageAsync_SavesMessageAndReturnsDto()
    {
        var user = CreateUser("chat-author@test.com", "Author");
        var whiteboard = await CreateWhiteboardAsync(user.Id, "Chat Board");

        var result = await _service.SendMessageAsync(whiteboard.Id, user.Id, " Hello chat ");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Hello chat", result.Data!.Content);
        Assert.Equal("Author", result.Data.UserDisplayName);
        Assert.Single(_context.WhiteboardChatMessages);
    }

    [Fact]
    public async Task SendMessageAsync_MessageExceeding4000Characters_IsRejected()
    {
        var user = CreateUser("chat-limit@test.com", "Author");
        var whiteboard = await CreateWhiteboardAsync(user.Id, "Chat Board");

        var result = await _service.SendMessageAsync(whiteboard.Id, user.Id, new string('a', 4001));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("4000"));
    }

    [Fact]
    public async Task SendMessageAsync_HtmlContent_IsStoredAsRawText()
    {
        var user = CreateUser("chat-xss@test.com", "Author");
        var whiteboard = await CreateWhiteboardAsync(user.Id, "Chat XSS Board");

        var result = await _service.SendMessageAsync(whiteboard.Id, user.Id, "<script>alert('xss')</script>");

        Assert.True(result.Succeeded);
        Assert.Equal("<script>alert('xss')</script>", result.Data!.Content);
        Assert.Equal(result.Data.Content, _context.WhiteboardChatMessages.Single().Content);
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsMessagesOrderedByCreatedAtDescending()
    {
        var user = CreateUser("chat-order@test.com", "Author");
        var whiteboard = await CreateWhiteboardAsync(user.Id, "Chat Board");

        _context.WhiteboardChatMessages.AddRange(
            new WhiteboardChatMessage
            {
                WhiteboardId = whiteboard.Id,
                UserId = user.Id,
                Content = "Older",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new WhiteboardChatMessage
            {
                WhiteboardId = whiteboard.Id,
                UserId = user.Id,
                Content = "Newer",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
        await _context.SaveChangesAsync();

        var result = await _service.GetMessagesAsync(whiteboard.Id, 1, 20);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("Newer", result.Data[0].Content);
        Assert.Equal("Older", result.Data[1].Content);
    }

    [Fact]
    public async Task GetMessagesAsync_AppliesPaging()
    {
        var user = CreateUser("chat-page@test.com", "Author");
        var whiteboard = await CreateWhiteboardAsync(user.Id, "Chat Board");

        for (var i = 0; i < 3; i++)
        {
            _context.WhiteboardChatMessages.Add(new WhiteboardChatMessage
            {
                WhiteboardId = whiteboard.Id,
                UserId = user.Id,
                Content = $"Message {i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await _context.SaveChangesAsync();

        var result = await _service.GetMessagesAsync(whiteboard.Id, 2, 1);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
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

    private async Task<Whiteboard> CreateWhiteboardAsync(string ownerId, string title)
    {
        var whiteboard = new Whiteboard
        {
            Title = title,
            OwnerId = ownerId,
            CurrentPresenterId = ownerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Whiteboards.Add(whiteboard);
        await _context.SaveChangesAsync();
        return whiteboard;
    }
}
