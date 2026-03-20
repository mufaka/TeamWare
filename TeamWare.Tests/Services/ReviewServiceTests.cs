using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class ReviewServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly NotificationService _notificationService;
    private readonly ReviewService _reviewService;
    private readonly TaskService _taskService;
    private readonly ProjectService _projectService;
    private readonly ActivityLogService _activityLogService;
    private readonly InboxService _inboxService;

    public ReviewServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _notificationService = new NotificationService(_context);
        _activityLogService = new ActivityLogService(_context);
        _projectService = new ProjectService(_context);
        _taskService = new TaskService(_context, _activityLogService, _notificationService);
        _inboxService = new InboxService(_context, _taskService, _notificationService);
        _reviewService = new ReviewService(_context, _notificationService);
    }

    private ApplicationUser CreateUser(string email = "test@test.com", string displayName = "Test User")
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

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner()
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject("Test Project", null, owner.Id);
        return (result.Data!, owner);
    }

    private async Task<TaskItem> CreateAndAssignTask(
        int projectId, string userId, string title = "Test Task",
        bool isNextAction = false, bool isSomedayMaybe = false,
        TaskItemStatus status = TaskItemStatus.ToDo)
    {
        var result = await _taskService.CreateTask(projectId, title, null,
            TaskItemPriority.Medium, null, userId);
        var task = result.Data!;

        // Assign to user
        _context.TaskAssignments.Add(new TaskAssignment
        {
            TaskItemId = task.Id,
            UserId = userId
        });

        if (isNextAction) task.IsNextAction = true;
        if (isSomedayMaybe) task.IsSomedayMaybe = true;
        if (status != TaskItemStatus.ToDo) task.Status = status;

        await _context.SaveChangesAsync();
        return task;
    }

    // --- StartReview ---

    [Fact]
    public async Task StartReview_EmptyUserId_ReturnsFailure()
    {
        var result = await _reviewService.StartReview("");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    [Fact]
    public async Task StartReview_NoData_ReturnsEmptyReviewData()
    {
        var user = CreateUser();

        var result = await _reviewService.StartReview(user.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.UnprocessedInboxItems);
        Assert.Empty(result.Data.ActiveTasks);
        Assert.Empty(result.Data.NextActions);
        Assert.Empty(result.Data.SomedayMaybeItems);
        Assert.Null(result.Data.LastReviewDate);
    }

    [Fact]
    public async Task StartReview_GathersUnprocessedInboxItems()
    {
        var user = CreateUser();
        _context.InboxItems.Add(new InboxItem { UserId = user.Id, Title = "Unprocessed item", Status = InboxItemStatus.Unprocessed });
        _context.InboxItems.Add(new InboxItem { UserId = user.Id, Title = "Processed item", Status = InboxItemStatus.Processed });
        _context.InboxItems.Add(new InboxItem { UserId = user.Id, Title = "Dismissed item", Status = InboxItemStatus.Dismissed });
        await _context.SaveChangesAsync();

        var result = await _reviewService.StartReview(user.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.UnprocessedInboxItems);
        Assert.Equal("Unprocessed item", result.Data.UnprocessedInboxItems[0].Title);
    }

    [Fact]
    public async Task StartReview_GathersActiveTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateAndAssignTask(project.Id, owner.Id, "Active task");
        await CreateAndAssignTask(project.Id, owner.Id, "Done task", status: TaskItemStatus.Done);
        await CreateAndAssignTask(project.Id, owner.Id, "Next action task", isNextAction: true);
        await CreateAndAssignTask(project.Id, owner.Id, "Someday task", isSomedayMaybe: true);

        var result = await _reviewService.StartReview(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.ActiveTasks);
        Assert.Equal("Active task", result.Data.ActiveTasks[0].Title);
    }

    [Fact]
    public async Task StartReview_GathersNextActions()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateAndAssignTask(project.Id, owner.Id, "Next action 1", isNextAction: true);
        await CreateAndAssignTask(project.Id, owner.Id, "Next action 2", isNextAction: true);
        await CreateAndAssignTask(project.Id, owner.Id, "Regular task");

        var result = await _reviewService.StartReview(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.NextActions.Count);
    }

    [Fact]
    public async Task StartReview_GathersSomedayMaybeItems()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateAndAssignTask(project.Id, owner.Id, "Someday item 1", isSomedayMaybe: true);
        await CreateAndAssignTask(project.Id, owner.Id, "Someday item 2", isSomedayMaybe: true);
        await CreateAndAssignTask(project.Id, owner.Id, "Regular task");

        var result = await _reviewService.StartReview(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.SomedayMaybeItems.Count);
    }

    [Fact]
    public async Task StartReview_ExcludesDoneTasks_FromAllCategories()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateAndAssignTask(project.Id, owner.Id, "Done active", status: TaskItemStatus.Done);
        await CreateAndAssignTask(project.Id, owner.Id, "Done next action", isNextAction: true, status: TaskItemStatus.Done);
        await CreateAndAssignTask(project.Id, owner.Id, "Done someday", isSomedayMaybe: true, status: TaskItemStatus.Done);

        var result = await _reviewService.StartReview(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!.ActiveTasks);
        Assert.Empty(result.Data.NextActions);
        Assert.Empty(result.Data.SomedayMaybeItems);
    }

    [Fact]
    public async Task StartReview_DoesNotIncludeOtherUsersData()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var otherUser = CreateUser($"other-{Guid.NewGuid():N}@test.com", "Other");
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = otherUser.Id, Role = ProjectRole.Member });
        await _context.SaveChangesAsync();

        await CreateAndAssignTask(project.Id, otherUser.Id, "Other user's task");
        _context.InboxItems.Add(new InboxItem { UserId = otherUser.Id, Title = "Other inbox" });
        await _context.SaveChangesAsync();

        var result = await _reviewService.StartReview(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!.ActiveTasks);
        Assert.Empty(result.Data.UnprocessedInboxItems);
    }

    [Fact]
    public async Task StartReview_IncludesLastReviewDate()
    {
        var user = CreateUser();
        var completedAt = DateTime.UtcNow.AddDays(-3);
        _context.UserReviews.Add(new UserReview { UserId = user.Id, CompletedAt = completedAt });
        await _context.SaveChangesAsync();

        var result = await _reviewService.StartReview(user.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data!.LastReviewDate);
        Assert.Equal(completedAt, result.Data.LastReviewDate);
    }

    // --- CompleteReview ---

    [Fact]
    public async Task CompleteReview_EmptyUserId_ReturnsFailure()
    {
        var result = await _reviewService.CompleteReview("");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    [Fact]
    public async Task CompleteReview_NotesTooLong_ReturnsFailure()
    {
        var user = CreateUser();
        var result = await _reviewService.CompleteReview(user.Id, new string('a', 2001));

        Assert.False(result.Succeeded);
        Assert.Contains("Notes must not exceed 2000 characters.", result.Errors);
    }

    [Fact]
    public async Task CompleteReview_CreatesReviewRecord()
    {
        var user = CreateUser();

        var result = await _reviewService.CompleteReview(user.Id, "Review notes");

        Assert.True(result.Succeeded);

        var review = await _context.UserReviews.FirstOrDefaultAsync(r => r.UserId == user.Id);
        Assert.NotNull(review);
        Assert.Equal("Review notes", review.Notes);
        Assert.True(review.CompletedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task CompleteReview_WithoutNotes_Succeeds()
    {
        var user = CreateUser();

        var result = await _reviewService.CompleteReview(user.Id);

        Assert.True(result.Succeeded);

        var review = await _context.UserReviews.FirstOrDefaultAsync(r => r.UserId == user.Id);
        Assert.NotNull(review);
        Assert.Null(review.Notes);
    }

    [Fact]
    public async Task CompleteReview_MultipleReviews_AllPersisted()
    {
        var user = CreateUser();

        await _reviewService.CompleteReview(user.Id, "First review");
        await _reviewService.CompleteReview(user.Id, "Second review");

        var reviews = await _context.UserReviews.Where(r => r.UserId == user.Id).ToListAsync();
        Assert.Equal(2, reviews.Count);
    }

    // --- GetLastReviewDate ---

    [Fact]
    public async Task GetLastReviewDate_NoReviews_ReturnsNull()
    {
        var user = CreateUser();

        var result = await _reviewService.GetLastReviewDate(user.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLastReviewDate_ReturnsLatestReview()
    {
        var user = CreateUser();
        var older = DateTime.UtcNow.AddDays(-14);
        var newer = DateTime.UtcNow.AddDays(-1);

        _context.UserReviews.Add(new UserReview { UserId = user.Id, CompletedAt = older });
        _context.UserReviews.Add(new UserReview { UserId = user.Id, CompletedAt = newer });
        await _context.SaveChangesAsync();

        var result = await _reviewService.GetLastReviewDate(user.Id);

        Assert.NotNull(result);
        Assert.Equal(newer, result);
    }

    // --- IsReviewDue ---

    [Fact]
    public async Task IsReviewDue_NoReviews_ReturnsTrue()
    {
        var user = CreateUser();

        var result = await _reviewService.IsReviewDue(user.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task IsReviewDue_RecentReview_ReturnsFalse()
    {
        var user = CreateUser();
        _context.UserReviews.Add(new UserReview { UserId = user.Id, CompletedAt = DateTime.UtcNow.AddDays(-2) });
        await _context.SaveChangesAsync();

        var result = await _reviewService.IsReviewDue(user.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task IsReviewDue_OldReview_ReturnsTrue()
    {
        var user = CreateUser();
        _context.UserReviews.Add(new UserReview { UserId = user.Id, CompletedAt = DateTime.UtcNow.AddDays(-10) });
        await _context.SaveChangesAsync();

        var result = await _reviewService.IsReviewDue(user.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task IsReviewDue_ExactlyOnBoundary_ReturnsTrue()
    {
        var user = CreateUser();
        _context.UserReviews.Add(new UserReview { UserId = user.Id, CompletedAt = DateTime.UtcNow.AddDays(-7) });
        await _context.SaveChangesAsync();

        var result = await _reviewService.IsReviewDue(user.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task IsReviewDue_CustomInterval_Respected()
    {
        var user = CreateUser();
        _context.UserReviews.Add(new UserReview { UserId = user.Id, CompletedAt = DateTime.UtcNow.AddDays(-3) });
        await _context.SaveChangesAsync();

        var dueWithShortInterval = await _reviewService.IsReviewDue(user.Id, 2);
        var notDueWithLongInterval = await _reviewService.IsReviewDue(user.Id, 14);

        Assert.True(dueWithShortInterval);
        Assert.False(notDueWithLongInterval);
    }

    // --- Integration: Review data includes tasks from active projects ---

    [Fact]
    public async Task StartReview_IncludesTasksFromMultipleProjects()
    {
        var (project1, owner) = await CreateProjectWithOwner();
        var project2Result = await _projectService.CreateProject("Project 2", null, owner.Id);
        var project2 = project2Result.Data!;

        await CreateAndAssignTask(project1.Id, owner.Id, "Task in project 1");
        await CreateAndAssignTask(project2.Id, owner.Id, "Task in project 2");

        var result = await _reviewService.StartReview(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.ActiveTasks.Count);
    }

    [Fact]
    public async Task StartReview_ActiveTasksIncludeProjectNavigation()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateAndAssignTask(project.Id, owner.Id, "Task with project");

        var result = await _reviewService.StartReview(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.ActiveTasks);
        Assert.NotNull(result.Data.ActiveTasks[0].Project);
        Assert.Equal("Test Project", result.Data.ActiveTasks[0].Project.Name);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
