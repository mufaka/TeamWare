using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class CommentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly CommentService _commentService;
    private readonly ProjectService _projectService;
    private readonly ActivityLogService _activityLogService;
    private readonly TaskService _taskService;

    public CommentServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _commentService = new CommentService(_context);
        _activityLogService = new ActivityLogService(_context);
        _taskService = new TaskService(_context, _activityLogService);
        _projectService = new ProjectService(_context);
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

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner(
        string projectName = "Test Project")
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject(projectName, null, owner.Id);
        return (result.Data!, owner);
    }

    private async Task<TaskItem> CreateTestTask(int projectId, string userId, string title = "Test Task")
    {
        var result = await _taskService.CreateTask(projectId, title, null, TaskItemPriority.Medium, null, userId);
        return result.Data!;
    }

    private async Task<ApplicationUser> AddProjectMember(int projectId, string ownerUserId,
        string memberEmail = "member@test.com", string displayName = "Member")
    {
        var member = CreateUser(memberEmail, displayName);
        var memberService = new ProjectMemberService(_context);
        await memberService.InviteMember(projectId, member.Id, ownerUserId);
        return member;
    }

    // --- AddComment ---

    [Fact]
    public async Task AddComment_Succeeds_WhenProjectMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        var result = await _commentService.AddComment(task.Id, "Hello, this is a comment.", owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Hello, this is a comment.", result.Data.Content);
        Assert.Equal(owner.Id, result.Data.AuthorId);
        Assert.Equal(task.Id, result.Data.TaskItemId);
    }

    [Fact]
    public async Task AddComment_Succeeds_AsNonOwnerMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddProjectMember(project.Id, owner.Id, $"member-{Guid.NewGuid():N}@test.com");
        var task = await CreateTestTask(project.Id, owner.Id);

        var result = await _commentService.AddComment(task.Id, "Member comment", member.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Member comment", result.Data!.Content);
    }

    [Fact]
    public async Task AddComment_Fails_WhenNotProjectMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        var outsider = CreateUser("outsider@test.com", "Outsider");

        var result = await _commentService.AddComment(task.Id, "Unauthorized comment", outsider.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("project member", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddComment_Fails_WhenContentEmpty()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        var result = await _commentService.AddComment(task.Id, "", owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("required", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddComment_Fails_WhenContentWhitespace()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        var result = await _commentService.AddComment(task.Id, "   ", owner.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AddComment_Fails_WhenTaskNotFound()
    {
        var result = await _commentService.AddComment(9999, "Comment", "some-user-id");

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddComment_TrimsContent()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        var result = await _commentService.AddComment(task.Id, "  spaced out  ", owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("spaced out", result.Data!.Content);
    }

    [Fact]
    public async Task AddComment_SetsTimestamps()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        var before = DateTime.UtcNow;

        var result = await _commentService.AddComment(task.Id, "Timestamped", owner.Id);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.CreatedAt >= before);
        Assert.True(result.Data!.UpdatedAt >= before);
    }

    [Fact]
    public async Task AddComment_LoadsAuthor()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        var result = await _commentService.AddComment(task.Id, "Author check", owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data!.Author);
        Assert.Equal(owner.DisplayName, result.Data.Author.DisplayName);
    }

    // --- EditComment ---

    [Fact]
    public async Task EditComment_Succeeds_WhenAuthor()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        var addResult = await _commentService.AddComment(task.Id, "Original", owner.Id);

        var editResult = await _commentService.EditComment(addResult.Data!.Id, "Updated content", owner.Id);

        Assert.True(editResult.Succeeded);
        Assert.Equal("Updated content", editResult.Data!.Content);
    }

    [Fact]
    public async Task EditComment_Fails_WhenNotAuthor()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddProjectMember(project.Id, owner.Id, $"member-{Guid.NewGuid():N}@test.com");
        var task = await CreateTestTask(project.Id, owner.Id);
        var addResult = await _commentService.AddComment(task.Id, "Owner comment", owner.Id);

        var editResult = await _commentService.EditComment(addResult.Data!.Id, "Hijacked!", member.Id);

        Assert.False(editResult.Succeeded);
        Assert.Contains("your own", editResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditComment_Fails_WhenContentEmpty()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        var addResult = await _commentService.AddComment(task.Id, "Original", owner.Id);

        var editResult = await _commentService.EditComment(addResult.Data!.Id, "", owner.Id);

        Assert.False(editResult.Succeeded);
    }

    [Fact]
    public async Task EditComment_Fails_WhenNotFound()
    {
        var result = await _commentService.EditComment(9999, "New content", "some-user-id");

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditComment_UpdatesTimestamp()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        var addResult = await _commentService.AddComment(task.Id, "Original", owner.Id);
        var originalUpdatedAt = addResult.Data!.UpdatedAt;

        await Task.Delay(10); // ensure different timestamp
        var editResult = await _commentService.EditComment(addResult.Data.Id, "Updated", owner.Id);

        Assert.True(editResult.Succeeded);
        Assert.True(editResult.Data!.UpdatedAt >= originalUpdatedAt);
    }

    [Fact]
    public async Task EditComment_TrimsContent()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        var addResult = await _commentService.AddComment(task.Id, "Original", owner.Id);

        var editResult = await _commentService.EditComment(addResult.Data!.Id, "  trimmed  ", owner.Id);

        Assert.True(editResult.Succeeded);
        Assert.Equal("trimmed", editResult.Data!.Content);
    }

    // --- DeleteComment ---

    [Fact]
    public async Task DeleteComment_Succeeds_WhenAuthor()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        var addResult = await _commentService.AddComment(task.Id, "To delete", owner.Id);

        var deleteResult = await _commentService.DeleteComment(addResult.Data!.Id, owner.Id);

        Assert.True(deleteResult.Succeeded);
        var remaining = await _context.Comments.ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteComment_Fails_WhenNotAuthor()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddProjectMember(project.Id, owner.Id, $"member-{Guid.NewGuid():N}@test.com");
        var task = await CreateTestTask(project.Id, owner.Id);
        var addResult = await _commentService.AddComment(task.Id, "Owner comment", owner.Id);

        var deleteResult = await _commentService.DeleteComment(addResult.Data!.Id, member.Id);

        Assert.False(deleteResult.Succeeded);
        Assert.Contains("your own", deleteResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteComment_Fails_WhenNotFound()
    {
        var result = await _commentService.DeleteComment(9999, "some-user-id");

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    // --- GetCommentsForTask ---

    [Fact]
    public async Task GetCommentsForTask_ReturnsComments_OrderedByDate()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _commentService.AddComment(task.Id, "First", owner.Id);
        await _commentService.AddComment(task.Id, "Second", owner.Id);
        await _commentService.AddComment(task.Id, "Third", owner.Id);

        var result = await _commentService.GetCommentsForTask(task.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.Count);
        Assert.Equal("First", result.Data[0].Content);
        Assert.Equal("Second", result.Data[1].Content);
        Assert.Equal("Third", result.Data[2].Content);
    }

    [Fact]
    public async Task GetCommentsForTask_ReturnsEmpty_WhenNoComments()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        var result = await _commentService.GetCommentsForTask(task.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetCommentsForTask_IncludesAuthor()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        await _commentService.AddComment(task.Id, "With author", owner.Id);

        var result = await _commentService.GetCommentsForTask(task.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data![0].Author);
        Assert.Equal(owner.DisplayName, result.Data[0].Author.DisplayName);
    }

    [Fact]
    public async Task GetCommentsForTask_Fails_WhenTaskNotFound()
    {
        var result = await _commentService.GetCommentsForTask(9999, "some-user-id");

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCommentsForTask_Fails_WhenNotProjectMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        var outsider = CreateUser("outsider@test.com", "Outsider");

        var result = await _commentService.GetCommentsForTask(task.Id, outsider.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("project member", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCommentsForTask_OnlyReturnsCommentsForSpecifiedTask()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task1 = await CreateTestTask(project.Id, owner.Id, "Task 1");
        var task2 = await CreateTestTask(project.Id, owner.Id, "Task 2");

        await _commentService.AddComment(task1.Id, "Comment on task 1", owner.Id);
        await _commentService.AddComment(task2.Id, "Comment on task 2", owner.Id);

        var result = await _commentService.GetCommentsForTask(task1.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Comment on task 1", result.Data![0].Content);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
