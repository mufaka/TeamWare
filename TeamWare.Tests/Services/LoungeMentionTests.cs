using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class LoungeMentionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly LoungeService _loungeService;
    private readonly NotificationService _notificationService;
    private readonly ProjectService _projectService;
    private readonly ProjectMemberService _memberService;

    public LoungeMentionTests()
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

    private ApplicationUser CreateUser(string email, string displayName = "Test User")
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

    // =============================================
    // 19.1 - Mention Parsing Tests (TEST-12)
    // =============================================

    [Fact]
    public void ExtractMentionedUsernames_SingleMention_ExtractsUsername()
    {
        var usernames = LoungeService.ExtractMentionedUsernames("Hello @alice how are you?");

        Assert.Single(usernames);
        Assert.Equal("alice", usernames[0]);
    }

    [Fact]
    public void ExtractMentionedUsernames_MultipleMentions_ExtractsAllUsernames()
    {
        var usernames = LoungeService.ExtractMentionedUsernames("@alice and @bob check this out @charlie");

        Assert.Equal(3, usernames.Count);
        Assert.Contains("alice", usernames);
        Assert.Contains("bob", usernames);
        Assert.Contains("charlie", usernames);
    }

    [Fact]
    public void ExtractMentionedUsernames_DuplicateMentions_ReturnsDistinct()
    {
        var usernames = LoungeService.ExtractMentionedUsernames("@alice and @alice again");

        Assert.Single(usernames);
        Assert.Equal("alice", usernames[0]);
    }

    [Fact]
    public void ExtractMentionedUsernames_NoMentions_ReturnsEmpty()
    {
        var usernames = LoungeService.ExtractMentionedUsernames("Hello everyone, no mentions here!");

        Assert.Empty(usernames);
    }

    [Fact]
    public void ExtractMentionedUsernames_EmailAddress_DoesNotMatch()
    {
        var usernames = LoungeService.ExtractMentionedUsernames("Send to user@example.com please");

        Assert.Empty(usernames);
    }

    [Fact]
    public void ExtractMentionedUsernames_MentionAtStartOfMessage_ExtractsUsername()
    {
        var usernames = LoungeService.ExtractMentionedUsernames("@admin please review this");

        Assert.Single(usernames);
        Assert.Equal("admin", usernames[0]);
    }

    [Fact]
    public void ExtractMentionedUsernames_MentionAtEndOfMessage_ExtractsUsername()
    {
        var usernames = LoungeService.ExtractMentionedUsernames("Please review this @admin");

        Assert.Single(usernames);
        Assert.Equal("admin", usernames[0]);
    }

    [Fact]
    public void ExtractMentionedUsernames_MentionAfterNewline_ExtractsUsername()
    {
        var usernames = LoungeService.ExtractMentionedUsernames("Hello\n@bob check this");

        Assert.Single(usernames);
        Assert.Equal("bob", usernames[0]);
    }

    // =============================================
    // 19.2 - Notification Integration Tests (TEST-12)
    // =============================================

    [Fact]
    public async Task SendMessage_WithMention_CreatesLoungeMentionNotification()
    {
        // Arrange: two users in #general
        var sender = CreateUser("sender@test.com", "Sender");
        var mentioned = CreateUser("mentioned@test.com", "Mentioned User");

        // Act: send a message mentioning the other user
        var result = await _loungeService.SendMessage(null, sender.Id, "Hey @mentioned@test.com check this");

        // The username is the email address, so let's use a simpler scenario
        // where the username matches
        Assert.True(result.Succeeded);

        // Verify notification was created
        var notifications = await _context.Notifications
            .Where(n => n.UserId == mentioned.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Single(notifications);
        Assert.Contains("Sender", notifications[0].Message);
        Assert.Contains("#general", notifications[0].Message);
        Assert.Equal(result.Data!.Id, notifications[0].ReferenceId);
    }

    [Fact]
    public async Task SendMessage_WithMention_InProjectRoom_CreatesNotificationWithProjectName()
    {
        // Arrange: project with two members
        var (project, owner) = await CreateProjectWithOwner("My Cool Project");
        var member = await AddProjectMember(project.Id, owner.Id, "member@test.com", "Team Member");

        // Act: owner mentions member in project room
        var result = await _loungeService.SendMessage(project.Id, owner.Id, $"Hey @{member.UserName} check the task");

        Assert.True(result.Succeeded);

        // Verify notification
        var notifications = await _context.Notifications
            .Where(n => n.UserId == member.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Single(notifications);
        Assert.Contains("Owner", notifications[0].Message);
        Assert.Contains("My Cool Project", notifications[0].Message);
        Assert.Equal(result.Data!.Id, notifications[0].ReferenceId);
    }

    [Fact]
    public async Task SendMessage_SelfMention_DoesNotCreateNotification()
    {
        // Arrange: user in #general
        var user = CreateUser("selfuser@test.com", "Self User");

        // Act: user mentions themselves
        var result = await _loungeService.SendMessage(null, user.Id, $"I am @{user.UserName} right here");

        Assert.True(result.Succeeded);

        // Verify no notification was created
        var notifications = await _context.Notifications
            .Where(n => n.UserId == user.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task SendMessage_NonMemberMention_InProjectRoom_DoesNotCreateNotification()
    {
        // Arrange: project with owner, and a non-member user
        var (project, owner) = await CreateProjectWithOwner();
        var nonMember = CreateUser("nonmember@test.com", "Non Member");

        // Act: owner mentions non-member in project room
        var result = await _loungeService.SendMessage(project.Id, owner.Id, $"Hey @{nonMember.UserName} join us");

        Assert.True(result.Succeeded);

        // Verify no notification was created for the non-member
        var notifications = await _context.Notifications
            .Where(n => n.UserId == nonMember.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task SendMessage_NonExistentUser_Mention_NoNotification()
    {
        // Arrange
        var user = CreateUser("user@test.com", "User");

        // Act: mention a user that doesn't exist
        var result = await _loungeService.SendMessage(null, user.Id, "Hey @nonexistentuser what's up");

        Assert.True(result.Succeeded);

        // Verify no notifications were created
        var notifications = await _context.Notifications
            .Where(n => n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task SendMessage_MemberMention_InProjectRoom_CreatesNotification()
    {
        // Arrange: project with owner and a member
        var (project, owner) = await CreateProjectWithOwner("Dev Project");
        var member = await AddProjectMember(project.Id, owner.Id, "dev@test.com", "Developer");

        // Act: owner mentions the member
        var result = await _loungeService.SendMessage(project.Id, owner.Id, $"@{member.UserName} please review");

        Assert.True(result.Succeeded);

        // Verify notification was created
        var notifications = await _context.Notifications
            .Where(n => n.UserId == member.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Single(notifications);
        Assert.Equal(result.Data!.Id, notifications[0].ReferenceId);
    }

    [Fact]
    public async Task SendMessage_MultipleMentions_CreatesMultipleNotifications()
    {
        // Arrange: project with three members
        var (project, owner) = await CreateProjectWithOwner("Team Project");
        var member1 = await AddProjectMember(project.Id, owner.Id, "alice@test.com", "Alice");
        var member2 = await AddProjectMember(project.Id, owner.Id, "bob@test.com", "Bob");

        // Act: owner mentions both members
        var result = await _loungeService.SendMessage(
            project.Id, owner.Id,
            $"@{member1.UserName} and @{member2.UserName} please check");

        Assert.True(result.Succeeded);

        // Verify notifications were created for both
        var aliceNotifs = await _context.Notifications
            .Where(n => n.UserId == member1.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();
        Assert.Single(aliceNotifs);

        var bobNotifs = await _context.Notifications
            .Where(n => n.UserId == member2.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();
        Assert.Single(bobNotifs);
    }

    [Fact]
    public async Task SendMessage_MixedValidAndInvalidMentions_OnlyCreatesValidNotifications()
    {
        // Arrange: project with owner and one member, plus a non-member
        var (project, owner) = await CreateProjectWithOwner("Mixed Project");
        var member = await AddProjectMember(project.Id, owner.Id, "valid@test.com", "Valid Member");
        var nonMember = CreateUser("invalid@test.com", "Non Member");

        // Act: mention both member and non-member
        var result = await _loungeService.SendMessage(
            project.Id, owner.Id,
            $"@{member.UserName} and @{nonMember.UserName} check this");

        Assert.True(result.Succeeded);

        // Verify only the valid member got a notification
        var memberNotifs = await _context.Notifications
            .Where(n => n.UserId == member.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();
        Assert.Single(memberNotifs);

        var nonMemberNotifs = await _context.Notifications
            .Where(n => n.UserId == nonMember.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();
        Assert.Empty(nonMemberNotifs);
    }

    [Fact]
    public async Task SendMessage_MentionInGeneral_AnyAuthenticatedUserGetsNotification()
    {
        // Arrange: two users (any authenticated user can be mentioned in #general)
        var sender = CreateUser("sender2@test.com", "Sender");
        var receiver = CreateUser("receiver@test.com", "Receiver");

        // Act: mention in #general
        var result = await _loungeService.SendMessage(null, sender.Id, $"@{receiver.UserName} hello!");

        Assert.True(result.Succeeded);

        // Verify notification was created
        var notifications = await _context.Notifications
            .Where(n => n.UserId == receiver.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Single(notifications);
        Assert.Contains("#general", notifications[0].Message);
    }

    [Fact]
    public async Task SendMessage_NoMentions_NoNotificationsCreated()
    {
        // Arrange
        var user = CreateUser("user2@test.com", "User");

        // Act: send a message without any mentions
        var result = await _loungeService.SendMessage(null, user.Id, "Just a normal message");

        Assert.True(result.Succeeded);

        // Verify no mention notifications were created
        var notifications = await _context.Notifications
            .Where(n => n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task SendMessage_DuplicateMentions_CreatesOnlyOneNotification()
    {
        // Arrange
        var sender = CreateUser("sender3@test.com", "Sender");
        var mentioned = CreateUser("mentioned2@test.com", "Mentioned");

        // Act: mention same user twice
        var result = await _loungeService.SendMessage(
            null, sender.Id,
            $"@{mentioned.UserName} hey @{mentioned.UserName} are you there?");

        Assert.True(result.Succeeded);

        // Verify only one notification
        var notifications = await _context.Notifications
            .Where(n => n.UserId == mentioned.Id && n.Type == NotificationType.LoungeMention)
            .ToListAsync();

        Assert.Single(notifications);
    }
}
