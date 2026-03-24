using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TeamWare.Web.Data;
using TeamWare.Web.Jobs;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Jobs;

public class LoungeRetentionJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly LoungeService _loungeService;
    private readonly NotificationService _notificationService;
    private readonly AttachmentService _attachmentService;
    private readonly FileStorageService _fileStorageService;
    private readonly LoungeRetentionJob _retentionJob;

    public LoungeRetentionJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _notificationService = new NotificationService(_context);
        _loungeService = new LoungeService(_context, _notificationService);

        // Seed ATTACHMENT_DIR for FileStorageService
        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = "ATTACHMENT_DIR",
            Value = Path.GetTempPath(),
            Description = "Test attachment directory"
        });
        _context.SaveChanges();

        var activityLogService = new AdminActivityLogService(_context);
        var configService = new GlobalConfigurationService(_context, activityLogService, new MemoryCache(new MemoryCacheOptions()));
        _fileStorageService = new FileStorageService(configService);
        _attachmentService = new AttachmentService(_context, _fileStorageService);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        var logger = loggerFactory.CreateLogger<LoungeRetentionJob>();

        _retentionJob = new LoungeRetentionJob(_loungeService, _attachmentService, _fileStorageService, logger);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // --- Helpers ---

    private ApplicationUser CreateUser(string email = "user@test.com", string displayName = "Test User")
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

    private LoungeMessage CreateMessage(string userId, int? projectId = null, DateTime? createdAt = null,
        bool isPinned = false)
    {
        var message = new LoungeMessage
        {
            ProjectId = projectId,
            UserId = userId,
            Content = "Test message",
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IsPinned = isPinned
        };
        _context.LoungeMessages.Add(message);
        _context.SaveChanges();
        return message;
    }

    // =============================================
    // 20.2 - LoungeRetentionJob Tests (TEST-11)
    // =============================================

    [Fact]
    public async Task Execute_InvokesCleanupExpiredMessages()
    {
        // Arrange: create an expired message (older than 30 days)
        var user = CreateUser();
        CreateMessage(user.Id, createdAt: DateTime.UtcNow.AddDays(-31));

        // Act
        await _retentionJob.Execute();

        // Assert: the expired message should be deleted
        var remainingMessages = await _context.LoungeMessages.CountAsync();
        Assert.Equal(0, remainingMessages);
    }

    [Fact]
    public async Task Execute_DeletesExpiredMessages_RetainsPinnedMessages()
    {
        // Arrange: one expired, one expired but pinned, one recent
        var user = CreateUser();
        var expiredMessage = CreateMessage(user.Id, createdAt: DateTime.UtcNow.AddDays(-31));
        var pinnedExpiredMessage = CreateMessage(user.Id, createdAt: DateTime.UtcNow.AddDays(-31), isPinned: true);
        var recentMessage = CreateMessage(user.Id, createdAt: DateTime.UtcNow.AddDays(-1));

        // Act
        await _retentionJob.Execute();

        // Assert: pinned and recent messages retained, expired deleted
        var remainingMessages = await _context.LoungeMessages.ToListAsync();
        Assert.Equal(2, remainingMessages.Count);
        Assert.Contains(remainingMessages, m => m.Id == pinnedExpiredMessage.Id);
        Assert.Contains(remainingMessages, m => m.Id == recentMessage.Id);
        Assert.DoesNotContain(remainingMessages, m => m.Id == expiredMessage.Id);
    }

    [Fact]
    public async Task Execute_CascadesReactions_WhenDeletingExpiredMessages()
    {
        // Arrange: expired message with a reaction
        var user = CreateUser();
        var expiredMessage = CreateMessage(user.Id, createdAt: DateTime.UtcNow.AddDays(-31));
        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = expiredMessage.Id,
            UserId = user.Id,
            ReactionType = "thumbsup",
            CreatedAt = DateTime.UtcNow.AddDays(-31)
        });
        _context.SaveChanges();

        // Act
        await _retentionJob.Execute();

        // Assert: both message and reaction deleted
        Assert.Equal(0, await _context.LoungeMessages.CountAsync());
        Assert.Equal(0, await _context.LoungeReactions.CountAsync());
    }

    [Fact]
    public async Task Execute_CleansOrphanedReadPositions()
    {
        // Arrange: expired message with a read position pointing to it
        var user = CreateUser();
        var expiredMessage = CreateMessage(user.Id, createdAt: DateTime.UtcNow.AddDays(-31));
        _context.LoungeReadPositions.Add(new LoungeReadPosition
        {
            UserId = user.Id,
            ProjectId = null,
            LastReadMessageId = expiredMessage.Id,
            UpdatedAt = DateTime.UtcNow.AddDays(-31)
        });
        _context.SaveChanges();

        // Act
        await _retentionJob.Execute();

        // Assert: orphaned read position cleaned up
        Assert.Equal(0, await _context.LoungeMessages.CountAsync());
        Assert.Equal(0, await _context.LoungeReadPositions.CountAsync());
    }

    [Fact]
    public async Task Execute_NoExpiredMessages_DoesNothing()
    {
        // Arrange: only recent messages
        var user = CreateUser();
        CreateMessage(user.Id, createdAt: DateTime.UtcNow.AddDays(-5));
        CreateMessage(user.Id, createdAt: DateTime.UtcNow);

        // Act
        await _retentionJob.Execute();

        // Assert: all messages retained
        Assert.Equal(2, await _context.LoungeMessages.CountAsync());
    }

    [Fact]
    public async Task Execute_EmptyDatabase_CompletesWithoutError()
    {
        // Act: should not throw
        await _retentionJob.Execute();

        // Assert: no messages existed, nothing to clean
        Assert.Equal(0, await _context.LoungeMessages.CountAsync());
    }
}
