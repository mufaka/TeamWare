using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class InboxServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly InboxService _inboxService;
    private readonly TaskService _taskService;
    private readonly ProjectService _projectService;
    private readonly ActivityLogService _activityLogService;
    private readonly NotificationService _notificationService;

    public InboxServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _activityLogService = new ActivityLogService(_context);
        _notificationService = new NotificationService(_context);
        _taskService = new TaskService(_context, _activityLogService, _notificationService);
        _projectService = new ProjectService(_context);
        _inboxService = new InboxService(_context, _taskService, _notificationService);
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

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner(string projectName = "Test Project")
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject(projectName, null, owner.Id);
        return (result.Data!, owner);
    }

    // --- AddItem ---

    [Fact]
    public async Task AddItem_Success()
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _inboxService.AddItem("Quick idea", "Details here", user.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Quick idea", result.Data.Title);
        Assert.Equal("Details here", result.Data.Description);
        Assert.Equal(InboxItemStatus.Unprocessed, result.Data.Status);
        Assert.Equal(user.Id, result.Data.UserId);
    }

    [Fact]
    public async Task AddItem_TrimsWhitespace()
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _inboxService.AddItem("  Trimmed Title  ", "  Trimmed Desc  ", user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Trimmed Title", result.Data!.Title);
        Assert.Equal("Trimmed Desc", result.Data.Description);
    }

    [Fact]
    public async Task AddItem_WithoutDescription_Success()
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _inboxService.AddItem("No description", null, user.Id);

        Assert.True(result.Succeeded);
        Assert.Null(result.Data!.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddItem_EmptyTitle_Fails(string? title)
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _inboxService.AddItem(title!, null, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("title is required"));
    }

    // --- DismissItem ---

    [Fact]
    public async Task DismissItem_Success()
    {
        var user = CreateUser("user@test.com", "User");
        var addResult = await _inboxService.AddItem("Dismiss me", null, user.Id);

        var result = await _inboxService.DismissItem(addResult.Data!.Id, user.Id);

        Assert.True(result.Succeeded);

        var item = await _context.InboxItems.FindAsync(addResult.Data.Id);
        Assert.Equal(InboxItemStatus.Dismissed, item!.Status);
    }

    [Fact]
    public async Task DismissItem_NotFound_Fails()
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _inboxService.DismissItem(999, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    [Fact]
    public async Task DismissItem_AlreadyProcessed_Fails()
    {
        var user = CreateUser("user@test.com", "User");
        var addResult = await _inboxService.AddItem("Already done", null, user.Id);
        await _inboxService.DismissItem(addResult.Data!.Id, user.Id);

        var result = await _inboxService.DismissItem(addResult.Data.Id, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("already been processed"));
    }

    [Fact]
    public async Task DismissItem_OtherUsersItem_Fails()
    {
        var user1 = CreateUser("user1@test.com", "User 1");
        var user2 = CreateUser("user2@test.com", "User 2");
        var addResult = await _inboxService.AddItem("User1's item", null, user1.Id);

        var result = await _inboxService.DismissItem(addResult.Data!.Id, user2.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    // --- ConvertToTask ---

    [Fact]
    public async Task ConvertToTask_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Convert me", "Task description", owner.Id);

        var result = await _inboxService.ConvertToTask(addResult.Data!.Id, project.Id,
            TaskItemPriority.High, DateTime.UtcNow.AddDays(7), false, false, owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Convert me", result.Data.Title);
        Assert.Equal("Task description", result.Data.Description);
        Assert.Equal(TaskItemPriority.High, result.Data.Priority);

        var item = await _context.InboxItems.FindAsync(addResult.Data.Id);
        Assert.Equal(InboxItemStatus.Processed, item!.Status);
        Assert.Equal(result.Data.Id, item.ConvertedToTaskId);
    }

    [Fact]
    public async Task ConvertToTask_WithDescriptionOverride_UsesProvidedDescription()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Convert me", "Original description", owner.Id);

        var result = await _inboxService.ConvertToTask(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, null, false, false, owner.Id, "Detailed task description");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Detailed task description", result.Data.Description);
    }

    [Fact]
    public async Task ConvertToTask_WithNullDescription_UsesInboxItemDescription()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Convert me", "Original description", owner.Id);

        var result = await _inboxService.ConvertToTask(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, null, false, false, owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Original description", result.Data.Description);
    }

    [Fact]
    public async Task ConvertToTask_AsNextAction_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Next action item", null, owner.Id);

        var result = await _inboxService.ConvertToTask(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, null, true, false, owner.Id);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.IsNextAction);
        Assert.False(result.Data.IsSomedayMaybe);
    }

    [Fact]
    public async Task ConvertToTask_AsSomedayMaybe_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Someday item", null, owner.Id);

        var result = await _inboxService.ConvertToTask(addResult.Data!.Id, project.Id,
            TaskItemPriority.Low, null, false, true, owner.Id);

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.IsNextAction);
        Assert.True(result.Data.IsSomedayMaybe);
    }

    [Fact]
    public async Task ConvertToTask_BothNextActionAndSomedayMaybe_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Both flags", null, owner.Id);

        var result = await _inboxService.ConvertToTask(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, null, true, true, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("cannot be both"));
    }

    [Fact]
    public async Task ConvertToTask_NotFound_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var result = await _inboxService.ConvertToTask(999, project.Id,
            TaskItemPriority.Medium, null, false, false, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    [Fact]
    public async Task ConvertToTask_AlreadyProcessed_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Already converted", null, owner.Id);
        await _inboxService.ConvertToTask(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, null, false, false, owner.Id);

        var result = await _inboxService.ConvertToTask(addResult.Data.Id, project.Id,
            TaskItemPriority.Medium, null, false, false, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("already been processed"));
    }

    [Fact]
    public async Task ConvertToTask_NotProjectMember_Fails()
    {
        var (project, _) = await CreateProjectWithOwner();
        var nonMember = CreateUser("nonmember@test.com", "Non Member");
        var addResult = await _inboxService.AddItem("Non member item", null, nonMember.Id);

        var result = await _inboxService.ConvertToTask(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, null, false, false, nonMember.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("project member"));
    }

    // --- ClarifyItem ---

    [Fact]
    public async Task ClarifyItem_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Clarify me", "Some details", owner.Id);

        var result = await _inboxService.ClarifyItem(addResult.Data!.Id, project.Id,
            TaskItemPriority.High, false, false, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(InboxItemStatus.Processed, result.Data!.Status);
        Assert.NotNull(result.Data.ConvertedToTaskId);

        var task = await _context.TaskItems.FindAsync(result.Data.ConvertedToTaskId);
        Assert.NotNull(task);
        Assert.Equal("Clarify me", task.Title);
        Assert.Equal(TaskItemPriority.High, task.Priority);
    }

    [Fact]
    public async Task ClarifyItem_AsNextAction_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Next action", null, owner.Id);

        var result = await _inboxService.ClarifyItem(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, true, false, owner.Id);

        Assert.True(result.Succeeded);
        var task = await _context.TaskItems.FindAsync(result.Data!.ConvertedToTaskId);
        Assert.NotNull(task);
        Assert.True(task.IsNextAction);
    }

    [Fact]
    public async Task ClarifyItem_AsSomedayMaybe_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Someday clarify", null, owner.Id);

        var result = await _inboxService.ClarifyItem(addResult.Data!.Id, project.Id,
            TaskItemPriority.Low, false, true, owner.Id);

        Assert.True(result.Succeeded);
        var task = await _context.TaskItems.FindAsync(result.Data!.ConvertedToTaskId);
        Assert.NotNull(task);
        Assert.True(task.IsSomedayMaybe);
    }

    [Fact]
    public async Task ClarifyItem_BothFlags_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Both flags", null, owner.Id);

        var result = await _inboxService.ClarifyItem(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, true, true, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("cannot be both"));
    }

    [Fact]
    public async Task ClarifyItem_NotFound_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var result = await _inboxService.ClarifyItem(999, project.Id,
            TaskItemPriority.Medium, false, false, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    [Fact]
    public async Task ClarifyItem_AlreadyProcessed_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Already clarified", null, owner.Id);
        await _inboxService.ClarifyItem(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, false, false, owner.Id);

        var result = await _inboxService.ClarifyItem(addResult.Data.Id, project.Id,
            TaskItemPriority.Medium, false, false, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("already been processed"));
    }

    [Fact]
    public async Task ClarifyItem_NotProjectMember_Fails()
    {
        var (project, _) = await CreateProjectWithOwner();
        var nonMember = CreateUser("nonmember@test.com", "Non Member");
        var addResult = await _inboxService.AddItem("Non member clarify", null, nonMember.Id);

        var result = await _inboxService.ClarifyItem(addResult.Data!.Id, project.Id,
            TaskItemPriority.Medium, false, false, nonMember.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("project member"));
    }

    // --- MoveToSomedayMaybe ---

    [Fact]
    public async Task MoveToSomedayMaybe_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Someday item", "Maybe later", owner.Id);

        var result = await _inboxService.MoveToSomedayMaybe(addResult.Data!.Id, project.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(InboxItemStatus.Processed, result.Data!.Status);
        Assert.NotNull(result.Data.ConvertedToTaskId);

        var task = await _context.TaskItems.FindAsync(result.Data.ConvertedToTaskId);
        Assert.NotNull(task);
        Assert.Equal("Someday item", task.Title);
        Assert.True(task.IsSomedayMaybe);
        Assert.Equal(TaskItemPriority.Low, task.Priority);
    }

    [Fact]
    public async Task MoveToSomedayMaybe_NotFound_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var result = await _inboxService.MoveToSomedayMaybe(999, project.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    [Fact]
    public async Task MoveToSomedayMaybe_AlreadyProcessed_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Already moved", null, owner.Id);
        await _inboxService.MoveToSomedayMaybe(addResult.Data!.Id, project.Id, owner.Id);

        var result = await _inboxService.MoveToSomedayMaybe(addResult.Data.Id, project.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("already been processed"));
    }

    [Fact]
    public async Task MoveToSomedayMaybe_NotProjectMember_Fails()
    {
        var (project, _) = await CreateProjectWithOwner();
        var nonMember = CreateUser("nonmember@test.com", "Non Member");
        var addResult = await _inboxService.AddItem("Non member someday", null, nonMember.Id);

        var result = await _inboxService.MoveToSomedayMaybe(addResult.Data!.Id, project.Id, nonMember.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("project member"));
    }

    // --- GetUnprocessedItems ---

    [Fact]
    public async Task GetUnprocessedItems_ReturnsOnlyUnprocessed()
    {
        var user = CreateUser("user@test.com", "User");
        await _inboxService.AddItem("Item 1", null, user.Id);
        await _inboxService.AddItem("Item 2", null, user.Id);
        var dismissResult = await _inboxService.AddItem("Item 3", null, user.Id);
        await _inboxService.DismissItem(dismissResult.Data!.Id, user.Id);

        var result = await _inboxService.GetUnprocessedItems(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, i => Assert.Equal(InboxItemStatus.Unprocessed, i.Status));
    }

    [Fact]
    public async Task GetUnprocessedItems_ReturnsOnlyCurrentUsersItems()
    {
        var user1 = CreateUser("user1@test.com", "User 1");
        var user2 = CreateUser("user2@test.com", "User 2");
        await _inboxService.AddItem("User1 Item", null, user1.Id);
        await _inboxService.AddItem("User2 Item", null, user2.Id);

        var result = await _inboxService.GetUnprocessedItems(user1.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("User1 Item", result.Data![0].Title);
    }

    [Fact]
    public async Task GetUnprocessedItems_OrderedByCreatedAtDescending()
    {
        var user = CreateUser("user@test.com", "User");
        await _inboxService.AddItem("First", null, user.Id);
        await _inboxService.AddItem("Second", null, user.Id);
        await _inboxService.AddItem("Third", null, user.Id);

        var result = await _inboxService.GetUnprocessedItems(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.Count);
        // Most recent first
        Assert.Equal("Third", result.Data[0].Title);
        Assert.Equal("Second", result.Data[1].Title);
        Assert.Equal("First", result.Data[2].Title);
    }

    [Fact]
    public async Task GetUnprocessedItems_EmptyForNewUser()
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _inboxService.GetUnprocessedItems(user.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    // --- GetUnprocessedCount ---

    [Fact]
    public async Task GetUnprocessedCount_ReturnsCorrectCount()
    {
        var user = CreateUser("user@test.com", "User");
        await _inboxService.AddItem("Item 1", null, user.Id);
        await _inboxService.AddItem("Item 2", null, user.Id);
        await _inboxService.AddItem("Item 3", null, user.Id);
        var dismissResult = await _inboxService.AddItem("Item 4", null, user.Id);
        await _inboxService.DismissItem(dismissResult.Data!.Id, user.Id);

        var result = await _inboxService.GetUnprocessedCount(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data);
    }

    [Fact]
    public async Task GetUnprocessedCount_ZeroForNewUser()
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _inboxService.GetUnprocessedCount(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data);
    }

    [Fact]
    public async Task GetUnprocessedCount_OnlyCountsCurrentUser()
    {
        var user1 = CreateUser("user1@test.com", "User 1");
        var user2 = CreateUser("user2@test.com", "User 2");
        await _inboxService.AddItem("User1 Item 1", null, user1.Id);
        await _inboxService.AddItem("User1 Item 2", null, user1.Id);
        await _inboxService.AddItem("User2 Item 1", null, user2.Id);

        var result = await _inboxService.GetUnprocessedCount(user1.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
