using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class LoungeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly LoungeService _loungeService;
    private readonly ProjectService _projectService;
    private readonly ProjectMemberService _memberService;
    private readonly NotificationService _notificationService;

    public LoungeServiceTests()
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
        _projectService = new ProjectService(_context);
        _memberService = new ProjectMemberService(_context);
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

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner(
        string projectName = "Test Project")
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject(projectName, null, owner.Id);
        return (result.Data!, owner);
    }

    private async Task<ApplicationUser> AddProjectMember(int projectId, string ownerUserId,
        string? memberEmail = null, string displayName = "Member")
    {
        memberEmail ??= $"member-{Guid.NewGuid():N}@test.com";
        var member = CreateUser(memberEmail, displayName);
        await _memberService.InviteMember(projectId, member.Id, ownerUserId);
        return member;
    }

    private async Task PromoteToProjectAdmin(int projectId, string userId, string ownerUserId)
    {
        await _memberService.UpdateMemberRole(projectId, userId, ProjectRole.Admin, ownerUserId);
    }

    // =============================================
    // 16.1 - Message Service Tests
    // =============================================

    // --- SendMessage ---

    [Fact]
    public async Task SendMessage_Succeeds_InGeneral_AnyUser()
    {
        var user = CreateUser();

        var result = await _loungeService.SendMessage(null, user.Id, "Hello general!");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Null(result.Data.ProjectId);
        Assert.Equal(user.Id, result.Data.UserId);
        Assert.Equal("Hello general!", result.Data.Content);
        Assert.NotNull(result.Data.User);
    }

    [Fact]
    public async Task SendMessage_Succeeds_InProjectRoom_WhenMember()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var result = await _loungeService.SendMessage(project.Id, owner.Id, "Hello project!");

        Assert.True(result.Succeeded);
        Assert.Equal(project.Id, result.Data!.ProjectId);
    }

    [Fact]
    public async Task SendMessage_Fails_InProjectRoom_WhenNotMember()
    {
        var (project, _) = await CreateProjectWithOwner();
        var outsider = CreateUser("outsider@test.com", "Outsider");

        var result = await _loungeService.SendMessage(project.Id, outsider.Id, "Unauthorized");

        Assert.False(result.Succeeded);
        Assert.Contains("access", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessage_Fails_WhenContentEmpty()
    {
        var user = CreateUser();

        var result = await _loungeService.SendMessage(null, user.Id, "");

        Assert.False(result.Succeeded);
        Assert.Contains("required", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessage_Fails_WhenContentWhitespace()
    {
        var user = CreateUser();

        var result = await _loungeService.SendMessage(null, user.Id, "   ");

        Assert.False(result.Succeeded);
        Assert.Contains("required", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessage_Fails_WhenContentExceeds4000Chars()
    {
        var user = CreateUser();
        var longContent = new string('x', 4001);

        var result = await _loungeService.SendMessage(null, user.Id, longContent);

        Assert.False(result.Succeeded);
        Assert.Contains("4000", result.Errors.First());
    }

    [Fact]
    public async Task SendMessage_TrimsContent()
    {
        var user = CreateUser();

        var result = await _loungeService.SendMessage(null, user.Id, "  trimmed  ");

        Assert.True(result.Succeeded);
        Assert.Equal("trimmed", result.Data!.Content);
    }

    [Fact]
    public async Task SendMessage_SetsTimestamp()
    {
        var user = CreateUser();
        var before = DateTime.UtcNow;

        var result = await _loungeService.SendMessage(null, user.Id, "Timestamped");

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.CreatedAt >= before);
    }

    // --- EditMessage ---

    [Fact]
    public async Task EditMessage_Succeeds_WhenAuthor()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Original");

        var editResult = await _loungeService.EditMessage(sendResult.Data!.Id, user.Id, "Updated");

        Assert.True(editResult.Succeeded);
        Assert.Equal("Updated", editResult.Data!.Content);
        Assert.True(editResult.Data.IsEdited);
        Assert.NotNull(editResult.Data.EditedAt);
    }

    [Fact]
    public async Task EditMessage_Fails_WhenNotAuthor()
    {
        var author = CreateUser("author@test.com", "Author");
        var other = CreateUser("other@test.com", "Other");
        var sendResult = await _loungeService.SendMessage(null, author.Id, "Original");

        var editResult = await _loungeService.EditMessage(sendResult.Data!.Id, other.Id, "Hijacked!");

        Assert.False(editResult.Succeeded);
        Assert.Contains("your own", editResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditMessage_Fails_WhenMessageNotFound()
    {
        var user = CreateUser();

        var result = await _loungeService.EditMessage(9999, user.Id, "Content");

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditMessage_Fails_WhenContentEmpty()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Original");

        var editResult = await _loungeService.EditMessage(sendResult.Data!.Id, user.Id, "");

        Assert.False(editResult.Succeeded);
        Assert.Contains("required", editResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditMessage_Fails_WhenContentExceeds4000Chars()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Original");
        var longContent = new string('x', 4001);

        var editResult = await _loungeService.EditMessage(sendResult.Data!.Id, user.Id, longContent);

        Assert.False(editResult.Succeeded);
        Assert.Contains("4000", editResult.Errors.First());
    }

    [Fact]
    public async Task EditMessage_TrimsContent()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Original");

        var editResult = await _loungeService.EditMessage(sendResult.Data!.Id, user.Id, "  trimmed  ");

        Assert.True(editResult.Succeeded);
        Assert.Equal("trimmed", editResult.Data!.Content);
    }

    [Fact]
    public async Task EditMessage_LoadsUser()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Original");

        var editResult = await _loungeService.EditMessage(sendResult.Data!.Id, user.Id, "Updated");

        Assert.True(editResult.Succeeded);
        Assert.NotNull(editResult.Data!.User);
        Assert.Equal(user.DisplayName, editResult.Data.User.DisplayName);
    }

    // --- DeleteMessage ---

    [Fact]
    public async Task DeleteMessage_Succeeds_WhenAuthor()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "To delete");

        var deleteResult = await _loungeService.DeleteMessage(sendResult.Data!.Id, user.Id);

        Assert.True(deleteResult.Succeeded);
        Assert.Null(await _context.LoungeMessages.FindAsync(sendResult.Data.Id));
    }

    [Fact]
    public async Task DeleteMessage_Succeeds_InProjectRoom_WhenProjectOwner()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddProjectMember(project.Id, owner.Id);
        var sendResult = await _loungeService.SendMessage(project.Id, member.Id, "Member message");

        var deleteResult = await _loungeService.DeleteMessage(sendResult.Data!.Id, owner.Id);

        Assert.True(deleteResult.Succeeded);
    }

    [Fact]
    public async Task DeleteMessage_Succeeds_InProjectRoom_WhenProjectAdmin()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var admin = await AddProjectMember(project.Id, owner.Id, "admin@test.com", "Admin");
        await PromoteToProjectAdmin(project.Id, admin.Id, owner.Id);
        var member = await AddProjectMember(project.Id, owner.Id);
        var sendResult = await _loungeService.SendMessage(project.Id, member.Id, "Member message");

        var deleteResult = await _loungeService.DeleteMessage(sendResult.Data!.Id, admin.Id);

        Assert.True(deleteResult.Succeeded);
    }

    [Fact]
    public async Task DeleteMessage_Fails_InProjectRoom_WhenRegularMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddProjectMember(project.Id, owner.Id);
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Owner message");

        var deleteResult = await _loungeService.DeleteMessage(sendResult.Data!.Id, member.Id);

        Assert.False(deleteResult.Succeeded);
        Assert.Contains("permission", deleteResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteMessage_Succeeds_InGeneral_WhenSiteAdmin()
    {
        var author = CreateUser("author@test.com", "Author");
        var siteAdmin = CreateUser("admin@test.com", "Admin");
        var sendResult = await _loungeService.SendMessage(null, author.Id, "To delete");

        var deleteResult = await _loungeService.DeleteMessage(sendResult.Data!.Id, siteAdmin.Id, isSiteAdmin: true);

        Assert.True(deleteResult.Succeeded);
    }

    [Fact]
    public async Task DeleteMessage_Fails_InGeneral_WhenNotAuthorOrSiteAdmin()
    {
        var author = CreateUser("author@test.com", "Author");
        var other = CreateUser("other@test.com", "Other");
        var sendResult = await _loungeService.SendMessage(null, author.Id, "To delete");

        var deleteResult = await _loungeService.DeleteMessage(sendResult.Data!.Id, other.Id);

        Assert.False(deleteResult.Succeeded);
        Assert.Contains("permission", deleteResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteMessage_Fails_WhenMessageNotFound()
    {
        var user = CreateUser();

        var result = await _loungeService.DeleteMessage(9999, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteMessage_CascadesReactions()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "To delete");
        await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, "thumbsup");

        await _loungeService.DeleteMessage(sendResult.Data.Id, user.Id);

        Assert.Empty(await _context.LoungeReactions.Where(r => r.LoungeMessageId == sendResult.Data.Id).ToListAsync());
    }

    // --- GetMessages ---

    [Fact]
    public async Task GetMessages_ReturnsMessagesInChronologicalOrder()
    {
        var user = CreateUser();
        await _loungeService.SendMessage(null, user.Id, "First");
        await _loungeService.SendMessage(null, user.Id, "Second");
        await _loungeService.SendMessage(null, user.Id, "Third");

        var result = await _loungeService.GetMessages(null, null, 50);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.Count);
        Assert.Equal("First", result.Data[0].Content);
        Assert.Equal("Second", result.Data[1].Content);
        Assert.Equal("Third", result.Data[2].Content);
    }

    [Fact]
    public async Task GetMessages_FiltersByProjectId()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var user = CreateUser();
        await _loungeService.SendMessage(null, user.Id, "General message");
        await _loungeService.SendMessage(project.Id, owner.Id, "Project message");

        var generalResult = await _loungeService.GetMessages(null, null, 50);
        var projectResult = await _loungeService.GetMessages(project.Id, null, 50);

        Assert.Single(generalResult.Data!);
        Assert.Equal("General message", generalResult.Data[0].Content);
        Assert.Single(projectResult.Data!);
        Assert.Equal("Project message", projectResult.Data[0].Content);
    }

    [Fact]
    public async Task GetMessages_PaginatesWithBefore()
    {
        var user = CreateUser();
        await _loungeService.SendMessage(null, user.Id, "First");
        await Task.Delay(10); // Ensure distinct timestamps
        var secondResult = await _loungeService.SendMessage(null, user.Id, "Second");
        await Task.Delay(10);
        await _loungeService.SendMessage(null, user.Id, "Third");

        var result = await _loungeService.GetMessages(null, secondResult.Data!.CreatedAt, 50);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("First", result.Data[0].Content);
    }

    [Fact]
    public async Task GetMessages_RespectsCount()
    {
        var user = CreateUser();
        for (int i = 0; i < 5; i++)
        {
            await _loungeService.SendMessage(null, user.Id, $"Message {i}");
        }

        var result = await _loungeService.GetMessages(null, null, 3);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.Count);
    }

    [Fact]
    public async Task GetMessages_IncludesUserAndReactions()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Hello");
        await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, "thumbsup");

        var result = await _loungeService.GetMessages(null, null, 50);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data![0].User);
        Assert.Single(result.Data[0].Reactions);
    }

    [Fact]
    public async Task GetMessages_DefaultsCountWhenZeroOrNegative()
    {
        var user = CreateUser();
        await _loungeService.SendMessage(null, user.Id, "Hello");

        var result = await _loungeService.GetMessages(null, null, 0);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
    }

    // --- GetMessage ---

    [Fact]
    public async Task GetMessage_ReturnsMessage()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Hello");

        var result = await _loungeService.GetMessage(sendResult.Data!.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello", result.Data!.Content);
        Assert.NotNull(result.Data.User);
    }

    [Fact]
    public async Task GetMessage_Fails_WhenNotFound()
    {
        var result = await _loungeService.GetMessage(9999);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    // =============================================
    // 16.2 - Pin Service Tests
    // =============================================

    [Fact]
    public async Task PinMessage_Succeeds_InProjectRoom_WhenOwner()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Pin me");

        var pinResult = await _loungeService.PinMessage(sendResult.Data!.Id, owner.Id);

        Assert.True(pinResult.Succeeded);
        Assert.True(pinResult.Data!.IsPinned);
        Assert.Equal(owner.Id, pinResult.Data.PinnedByUserId);
        Assert.NotNull(pinResult.Data.PinnedAt);
    }

    [Fact]
    public async Task PinMessage_Succeeds_InProjectRoom_WhenAdmin()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var admin = await AddProjectMember(project.Id, owner.Id, "admin@test.com", "Admin");
        await PromoteToProjectAdmin(project.Id, admin.Id, owner.Id);
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Pin me");

        var pinResult = await _loungeService.PinMessage(sendResult.Data!.Id, admin.Id);

        Assert.True(pinResult.Succeeded);
        Assert.True(pinResult.Data!.IsPinned);
    }

    [Fact]
    public async Task PinMessage_Fails_InProjectRoom_WhenRegularMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddProjectMember(project.Id, owner.Id);
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Pin me");

        var pinResult = await _loungeService.PinMessage(sendResult.Data!.Id, member.Id);

        Assert.False(pinResult.Succeeded);
        Assert.Contains("owners and admins", pinResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinMessage_Succeeds_InGeneral_WhenSiteAdmin()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Pin me");

        var pinResult = await _loungeService.PinMessage(sendResult.Data!.Id, user.Id, isSiteAdmin: true);

        Assert.True(pinResult.Succeeded);
        Assert.True(pinResult.Data!.IsPinned);
    }

    [Fact]
    public async Task PinMessage_Fails_InGeneral_WhenNotSiteAdmin()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Pin me");

        var pinResult = await _loungeService.PinMessage(sendResult.Data!.Id, user.Id);

        Assert.False(pinResult.Succeeded);
        Assert.Contains("site admins", pinResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinMessage_Fails_WhenAlreadyPinned()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Pin me");
        await _loungeService.PinMessage(sendResult.Data!.Id, owner.Id);

        var pinResult = await _loungeService.PinMessage(sendResult.Data.Id, owner.Id);

        Assert.False(pinResult.Succeeded);
        Assert.Contains("already pinned", pinResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinMessage_Fails_WhenMessageNotFound()
    {
        var user = CreateUser();

        var result = await _loungeService.PinMessage(9999, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnpinMessage_Succeeds_InProjectRoom_WhenOwner()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Pin me");
        await _loungeService.PinMessage(sendResult.Data!.Id, owner.Id);

        var unpinResult = await _loungeService.UnpinMessage(sendResult.Data.Id, owner.Id);

        Assert.True(unpinResult.Succeeded);
        Assert.False(unpinResult.Data!.IsPinned);
        Assert.Null(unpinResult.Data.PinnedByUserId);
        Assert.Null(unpinResult.Data.PinnedAt);
    }

    [Fact]
    public async Task UnpinMessage_Fails_InProjectRoom_WhenRegularMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddProjectMember(project.Id, owner.Id);
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Pin me");
        await _loungeService.PinMessage(sendResult.Data!.Id, owner.Id);

        var unpinResult = await _loungeService.UnpinMessage(sendResult.Data.Id, member.Id);

        Assert.False(unpinResult.Succeeded);
    }

    [Fact]
    public async Task UnpinMessage_Succeeds_InGeneral_WhenSiteAdmin()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Pin me");
        await _loungeService.PinMessage(sendResult.Data!.Id, user.Id, isSiteAdmin: true);

        var unpinResult = await _loungeService.UnpinMessage(sendResult.Data.Id, user.Id, isSiteAdmin: true);

        Assert.True(unpinResult.Succeeded);
        Assert.False(unpinResult.Data!.IsPinned);
    }

    [Fact]
    public async Task UnpinMessage_Fails_InGeneral_WhenNotSiteAdmin()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Pin me");
        await _loungeService.PinMessage(sendResult.Data!.Id, user.Id, isSiteAdmin: true);

        var unpinResult = await _loungeService.UnpinMessage(sendResult.Data.Id, user.Id);

        Assert.False(unpinResult.Succeeded);
        Assert.Contains("site admins", unpinResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnpinMessage_Fails_WhenNotPinned()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Not pinned");

        var unpinResult = await _loungeService.UnpinMessage(sendResult.Data!.Id, owner.Id);

        Assert.False(unpinResult.Succeeded);
        Assert.Contains("not pinned", unpinResult.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnpinMessage_Fails_WhenMessageNotFound()
    {
        var user = CreateUser();

        var result = await _loungeService.UnpinMessage(9999, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPinnedMessages_ReturnsOnlyPinned()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var send1 = await _loungeService.SendMessage(project.Id, owner.Id, "Pinned");
        await _loungeService.SendMessage(project.Id, owner.Id, "Not pinned");
        await _loungeService.PinMessage(send1.Data!.Id, owner.Id);

        var result = await _loungeService.GetPinnedMessages(project.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Pinned", result.Data[0].Content);
    }

    [Fact]
    public async Task GetPinnedMessages_FiltersByProjectId()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var user = CreateUser();

        var projectMsg = await _loungeService.SendMessage(project.Id, owner.Id, "Project pinned");
        await _loungeService.PinMessage(projectMsg.Data!.Id, owner.Id);

        var generalMsg = await _loungeService.SendMessage(null, user.Id, "General pinned");
        await _loungeService.PinMessage(generalMsg.Data!.Id, user.Id, isSiteAdmin: true);

        var projectPins = await _loungeService.GetPinnedMessages(project.Id);
        var generalPins = await _loungeService.GetPinnedMessages(null);

        Assert.Single(projectPins.Data!);
        Assert.Single(generalPins.Data!);
    }

    // =============================================
    // 16.3 - Reaction Service Tests
    // =============================================

    [Fact]
    public async Task ToggleReaction_AddsReaction()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "React to me");

        var result = await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, "thumbsup");

        Assert.True(result.Succeeded);
        Assert.Equal("thumbsup", result.Data!.ReactionType);
        Assert.Equal(1, await _context.LoungeReactions.CountAsync());
    }

    [Fact]
    public async Task ToggleReaction_RemovesExistingReaction()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "React to me");
        await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, "thumbsup");

        var result = await _loungeService.ToggleReaction(sendResult.Data.Id, user.Id, "thumbsup");

        Assert.True(result.Succeeded);
        Assert.Equal(0, await _context.LoungeReactions.CountAsync());
    }

    [Fact]
    public async Task ToggleReaction_Fails_WithInvalidReactionType()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "React to me");

        var result = await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, "invalid");

        Assert.False(result.Succeeded);
        Assert.Contains("Invalid reaction type", result.Errors.First());
    }

    [Fact]
    public async Task ToggleReaction_Fails_WhenReactionTypeEmpty()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "React to me");

        var result = await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, "");

        Assert.False(result.Succeeded);
        Assert.Contains("required", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToggleReaction_Fails_WhenMessageNotFound()
    {
        var user = CreateUser();

        var result = await _loungeService.ToggleReaction(9999, user.Id, "thumbsup");

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToggleReaction_Fails_InProjectRoom_WhenNotMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var outsider = CreateUser("outsider@test.com", "Outsider");
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "React to me");

        var result = await _loungeService.ToggleReaction(sendResult.Data!.Id, outsider.Id, "thumbsup");

        Assert.False(result.Succeeded);
        Assert.Contains("project member", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToggleReaction_Succeeds_InGeneral_AnyUser()
    {
        var author = CreateUser("author@test.com", "Author");
        var reactor = CreateUser("reactor@test.com", "Reactor");
        var sendResult = await _loungeService.SendMessage(null, author.Id, "React to me");

        var result = await _loungeService.ToggleReaction(sendResult.Data!.Id, reactor.Id, "heart");

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("thumbsup")]
    [InlineData("heart")]
    [InlineData("laugh")]
    [InlineData("rocket")]
    [InlineData("eyes")]
    public async Task ToggleReaction_AcceptsAllAllowedTypes(string reactionType)
    {
        var user = CreateUser($"{reactionType}@test.com", reactionType);
        var sendResult = await _loungeService.SendMessage(null, user.Id, "React to me");

        var result = await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, reactionType);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ToggleReaction_AllowsMultipleTypesPerUser()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "React to me");

        await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, "thumbsup");
        await _loungeService.ToggleReaction(sendResult.Data.Id, user.Id, "heart");

        Assert.Equal(2, await _context.LoungeReactions.CountAsync());
    }

    [Fact]
    public async Task GetReactionsForMessage_ReturnsSummaries()
    {
        var user1 = CreateUser("u1@test.com", "User1");
        var user2 = CreateUser("u2@test.com", "User2");
        var sendResult = await _loungeService.SendMessage(null, user1.Id, "React to me");

        await _loungeService.ToggleReaction(sendResult.Data!.Id, user1.Id, "thumbsup");
        await _loungeService.ToggleReaction(sendResult.Data.Id, user2.Id, "thumbsup");
        await _loungeService.ToggleReaction(sendResult.Data.Id, user1.Id, "heart");

        var result = await _loungeService.GetReactionsForMessage(sendResult.Data.Id, user1.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);

        var thumbsup = result.Data.First(r => r.ReactionType == "thumbsup");
        Assert.Equal(2, thumbsup.Count);
        Assert.True(thumbsup.CurrentUserReacted);

        var heart = result.Data.First(r => r.ReactionType == "heart");
        Assert.Equal(1, heart.Count);
        Assert.True(heart.CurrentUserReacted);
    }

    [Fact]
    public async Task GetReactionsForMessage_ShowsCurrentUserReacted_False()
    {
        var user1 = CreateUser("u1@test.com", "User1");
        var user2 = CreateUser("u2@test.com", "User2");
        var sendResult = await _loungeService.SendMessage(null, user1.Id, "React to me");
        await _loungeService.ToggleReaction(sendResult.Data!.Id, user1.Id, "thumbsup");

        var result = await _loungeService.GetReactionsForMessage(sendResult.Data.Id, user2.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.False(result.Data[0].CurrentUserReacted);
    }

    [Fact]
    public async Task GetReactionsForMessage_Fails_WhenMessageNotFound()
    {
        var result = await _loungeService.GetReactionsForMessage(9999);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    // =============================================
    // 16.4 - Unread Tracking Service Tests
    // =============================================

    [Fact]
    public async Task UpdateReadPosition_CreatesNewPosition()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Hello");

        var result = await _loungeService.UpdateReadPosition(user.Id, null, sendResult.Data!.Id);

        Assert.True(result.Succeeded);
        var position = await _context.LoungeReadPositions
            .FirstOrDefaultAsync(rp => rp.UserId == user.Id && rp.ProjectId == null);
        Assert.NotNull(position);
        Assert.Equal(sendResult.Data.Id, position.LastReadMessageId);
    }

    [Fact]
    public async Task UpdateReadPosition_AdvancesExistingPosition()
    {
        var user = CreateUser();
        var send1 = await _loungeService.SendMessage(null, user.Id, "First");
        var send2 = await _loungeService.SendMessage(null, user.Id, "Second");
        await _loungeService.UpdateReadPosition(user.Id, null, send1.Data!.Id);

        await _loungeService.UpdateReadPosition(user.Id, null, send2.Data!.Id);

        var position = await _context.LoungeReadPositions
            .FirstOrDefaultAsync(rp => rp.UserId == user.Id && rp.ProjectId == null);
        Assert.Equal(send2.Data.Id, position!.LastReadMessageId);
    }

    [Fact]
    public async Task UpdateReadPosition_DoesNotGoBackward()
    {
        var user = CreateUser();
        var send1 = await _loungeService.SendMessage(null, user.Id, "First");
        var send2 = await _loungeService.SendMessage(null, user.Id, "Second");
        await _loungeService.UpdateReadPosition(user.Id, null, send2.Data!.Id);

        await _loungeService.UpdateReadPosition(user.Id, null, send1.Data!.Id);

        var position = await _context.LoungeReadPositions
            .FirstOrDefaultAsync(rp => rp.UserId == user.Id && rp.ProjectId == null);
        Assert.Equal(send2.Data.Id, position!.LastReadMessageId);
    }

    [Fact]
    public async Task UpdateReadPosition_Fails_WhenMessageNotInRoom()
    {
        var user = CreateUser();
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Project message");

        // Try to set read position for #general using a project room message
        var result = await _loungeService.UpdateReadPosition(user.Id, null, sendResult.Data!.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUnreadCounts_ReturnsCorrectCounts()
    {
        var user = CreateUser();
        await _loungeService.SendMessage(null, user.Id, "Msg 1");
        await _loungeService.SendMessage(null, user.Id, "Msg 2");
        await _loungeService.SendMessage(null, user.Id, "Msg 3");

        var result = await _loungeService.GetUnreadCounts(user.Id);

        Assert.True(result.Succeeded);
        var generalRoom = result.Data!.FirstOrDefault(r => r.ProjectId == null);
        Assert.NotNull(generalRoom);
        Assert.Equal(3, generalRoom.Count);
    }

    [Fact]
    public async Task GetUnreadCounts_ExcludesReadMessages()
    {
        var user = CreateUser();
        var send1 = await _loungeService.SendMessage(null, user.Id, "Read");
        await _loungeService.SendMessage(null, user.Id, "Unread");
        await _loungeService.UpdateReadPosition(user.Id, null, send1.Data!.Id);

        var result = await _loungeService.GetUnreadCounts(user.Id);

        Assert.True(result.Succeeded);
        var generalRoom = result.Data!.FirstOrDefault(r => r.ProjectId == null);
        Assert.NotNull(generalRoom);
        Assert.Equal(1, generalRoom.Count);
    }

    [Fact]
    public async Task GetUnreadCounts_IncludesProjectRooms()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _loungeService.SendMessage(project.Id, owner.Id, "Project message");

        var result = await _loungeService.GetUnreadCounts(owner.Id);

        Assert.True(result.Succeeded);
        var projectRoom = result.Data!.FirstOrDefault(r => r.ProjectId == project.Id);
        Assert.NotNull(projectRoom);
        Assert.Equal(1, projectRoom.Count);
    }

    [Fact]
    public async Task GetUnreadCounts_OmitsRoomsWithNoUnread()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Read it");
        await _loungeService.UpdateReadPosition(user.Id, null, sendResult.Data!.Id);

        var result = await _loungeService.GetUnreadCounts(user.Id);

        Assert.True(result.Succeeded);
        Assert.Null(result.Data!.FirstOrDefault(r => r.ProjectId == null));
    }

    [Fact]
    public async Task GetReadPosition_ReturnsLastReadId()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Hello");
        await _loungeService.UpdateReadPosition(user.Id, null, sendResult.Data!.Id);

        var result = await _loungeService.GetReadPosition(user.Id, null);

        Assert.True(result.Succeeded);
        Assert.Equal(sendResult.Data.Id, result.Data);
    }

    [Fact]
    public async Task GetReadPosition_ReturnsNull_WhenNoPosition()
    {
        var user = CreateUser();

        var result = await _loungeService.GetReadPosition(user.Id, null);

        Assert.True(result.Succeeded);
        Assert.Null(result.Data);
    }

    // =============================================
    // 16.5 - Message-to-Task Conversion Tests
    // =============================================

    [Fact]
    public async Task CreateTaskFromMessage_Succeeds_InProjectRoom()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Build the feature");

        var result = await _loungeService.CreateTaskFromMessage(sendResult.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Build the feature", result.Data.Title);
        Assert.Contains("Build the feature", result.Data.Description!);
        Assert.Equal(project.Id, result.Data.ProjectId);
        Assert.Equal(owner.Id, result.Data.CreatedByUserId);
        Assert.Equal(TaskItemStatus.ToDo, result.Data.Status);
        Assert.Equal(TaskItemPriority.Medium, result.Data.Priority);
    }

    [Fact]
    public async Task CreateTaskFromMessage_SetsCreatedTaskId()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Build the feature");

        var result = await _loungeService.CreateTaskFromMessage(sendResult.Data!.Id, owner.Id);

        var updatedMessage = await _context.LoungeMessages.FindAsync(sendResult.Data.Id);
        Assert.Equal(result.Data!.Id, updatedMessage!.CreatedTaskId);
    }

    [Fact]
    public async Task CreateTaskFromMessage_TruncatesLongTitle()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var longContent = new string('x', 400);
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, longContent);

        var result = await _loungeService.CreateTaskFromMessage(sendResult.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.Title.Length <= 300);
        Assert.EndsWith("...", result.Data.Title);
    }

    [Fact]
    public async Task CreateTaskFromMessage_Fails_InGeneral()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "General message");

        var result = await _loungeService.CreateTaskFromMessage(sendResult.Data!.Id, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("project rooms", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTaskFromMessage_Fails_WhenNotProjectMember()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var outsider = CreateUser("outsider@test.com", "Outsider");
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Owner message");

        var result = await _loungeService.CreateTaskFromMessage(sendResult.Data!.Id, outsider.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("project member", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTaskFromMessage_Fails_WhenAlreadyConverted()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Convert me");
        await _loungeService.CreateTaskFromMessage(sendResult.Data!.Id, owner.Id);

        var result = await _loungeService.CreateTaskFromMessage(sendResult.Data.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("already been created", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTaskFromMessage_Fails_WhenMessageNotFound()
    {
        var user = CreateUser();

        var result = await _loungeService.CreateTaskFromMessage(9999, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTaskFromMessage_IncludesAuthorInDescription()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Do this thing");

        var result = await _loungeService.CreateTaskFromMessage(sendResult.Data!.Id, owner.Id);

        Assert.Contains("Owner", result.Data!.Description!);
    }

    // =============================================
    // 16.6 - Message Retention Tests
    // =============================================

    [Fact]
    public async Task CleanupExpiredMessages_DeletesOldMessages()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Old message");

        // Manually set the CreatedAt to 31 days ago
        var message = await _context.LoungeMessages.FindAsync(sendResult.Data!.Id);
        message!.CreatedAt = DateTime.UtcNow.AddDays(-31);
        await _context.SaveChangesAsync();

        var count = await _loungeService.CleanupExpiredMessages();

        Assert.Equal(1, count);
        Assert.Null(await _context.LoungeMessages.FindAsync(sendResult.Data.Id));
    }

    [Fact]
    public async Task CleanupExpiredMessages_RetainsPinnedMessages()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var sendResult = await _loungeService.SendMessage(project.Id, owner.Id, "Pinned old");
        await _loungeService.PinMessage(sendResult.Data!.Id, owner.Id);

        // Set to old
        var message = await _context.LoungeMessages.FindAsync(sendResult.Data.Id);
        message!.CreatedAt = DateTime.UtcNow.AddDays(-31);
        await _context.SaveChangesAsync();

        var count = await _loungeService.CleanupExpiredMessages();

        Assert.Equal(0, count);
        Assert.NotNull(await _context.LoungeMessages.FindAsync(sendResult.Data.Id));
    }

    [Fact]
    public async Task CleanupExpiredMessages_RetainsRecentMessages()
    {
        var user = CreateUser();
        await _loungeService.SendMessage(null, user.Id, "Recent message");

        var count = await _loungeService.CleanupExpiredMessages();

        Assert.Equal(0, count);
        Assert.Equal(1, await _context.LoungeMessages.CountAsync());
    }

    [Fact]
    public async Task CleanupExpiredMessages_CascadesReactions()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Old message");
        await _loungeService.ToggleReaction(sendResult.Data!.Id, user.Id, "thumbsup");

        var message = await _context.LoungeMessages.FindAsync(sendResult.Data.Id);
        message!.CreatedAt = DateTime.UtcNow.AddDays(-31);
        await _context.SaveChangesAsync();

        await _loungeService.CleanupExpiredMessages();

        Assert.Empty(await _context.LoungeReactions.ToListAsync());
    }

    [Fact]
    public async Task CleanupExpiredMessages_CleansOrphanedReadPositions()
    {
        var user = CreateUser();
        var sendResult = await _loungeService.SendMessage(null, user.Id, "Old message");
        await _loungeService.UpdateReadPosition(user.Id, null, sendResult.Data!.Id);

        var message = await _context.LoungeMessages.FindAsync(sendResult.Data.Id);
        message!.CreatedAt = DateTime.UtcNow.AddDays(-31);
        await _context.SaveChangesAsync();

        await _loungeService.CleanupExpiredMessages();

        // Read position should be removed since no messages remain
        Assert.Empty(await _context.LoungeReadPositions.ToListAsync());
    }

    [Fact]
    public async Task CleanupExpiredMessages_ReassignsReadPosition_WhenNewerMessageExists()
    {
        var user = CreateUser();
        var send1 = await _loungeService.SendMessage(null, user.Id, "Old message");
        var send2 = await _loungeService.SendMessage(null, user.Id, "Recent message");
        await _loungeService.UpdateReadPosition(user.Id, null, send1.Data!.Id);

        // Only make the first message old
        var message = await _context.LoungeMessages.FindAsync(send1.Data.Id);
        message!.CreatedAt = DateTime.UtcNow.AddDays(-31);
        await _context.SaveChangesAsync();

        await _loungeService.CleanupExpiredMessages();

        // Read position should remain but can't point to deleted message
        // Since send2 has a higher ID than send1, there's no earlier surviving message,
        // so the read position should be removed
        var positions = await _context.LoungeReadPositions.ToListAsync();
        // No surviving message with ID < send1.Data.Id, so position is removed
        Assert.Empty(positions);
    }

    [Fact]
    public async Task CleanupExpiredMessages_ReturnsZero_WhenNothingToClean()
    {
        var count = await _loungeService.CleanupExpiredMessages();

        Assert.Equal(0, count);
    }
}
