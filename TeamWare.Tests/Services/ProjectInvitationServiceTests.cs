using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class ProjectInvitationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectInvitationService _invitationService;
    private readonly NotificationService _notificationService;
    private readonly ProjectService _projectService;

    public ProjectInvitationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _notificationService = new NotificationService(_context);
        _invitationService = new ProjectInvitationService(_context, _notificationService);
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

    private async Task<Project> CreateProjectWithOwner(ApplicationUser owner, string name = "Test Project")
    {
        var result = await _projectService.CreateProject(name, null, owner.Id);
        return result.Data!;
    }

    private void AddMember(int projectId, string userId, ProjectRole role = ProjectRole.Admin)
    {
        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    // --- SendInvitation ---

    [Fact]
    public async Task SendInvitation_AsOwner_CreatesPendingInvitation()
    {
        var owner = CreateUser("owner@test.com", "Owner");
        var invitee = CreateUser("invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(InvitationStatus.Pending, result.Data.Status);
        Assert.Equal(project.Id, result.Data.ProjectId);
        Assert.Equal(invitee.Id, result.Data.InvitedUserId);
        Assert.Equal(owner.Id, result.Data.InvitedByUserId);
        Assert.Equal(ProjectRole.Member, result.Data.Role);
        Assert.Null(result.Data.RespondedAt);
    }

    [Fact]
    public async Task SendInvitation_AsAdmin_Succeeds()
    {
        var owner = CreateUser("owner2@test.com", "Owner");
        var admin = CreateUser("admin@test.com", "Admin");
        var invitee = CreateUser("invitee2@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        AddMember(project.Id, admin.Id, ProjectRole.Admin);

        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, admin.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SendInvitation_AsMember_Fails()
    {
        var owner = CreateUser("owner3@test.com", "Owner");
        var member = CreateUser("member@test.com", "Member");
        var invitee = CreateUser("invitee3@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        AddMember(project.Id, member.Id, ProjectRole.Member);

        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, member.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Only project owners and admins can invite members.", result.Errors);
    }

    [Fact]
    public async Task SendInvitation_NonMember_Fails()
    {
        var owner = CreateUser("owner4@test.com", "Owner");
        var stranger = CreateUser("stranger@test.com", "Stranger");
        var invitee = CreateUser("invitee4@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, stranger.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SendInvitation_ExistingMember_Fails()
    {
        var owner = CreateUser("owner5@test.com", "Owner");
        var existingMember = CreateUser("existing@test.com", "Existing");
        var project = await CreateProjectWithOwner(owner);
        AddMember(project.Id, existingMember.Id, ProjectRole.Member);

        var result = await _invitationService.SendInvitation(project.Id, existingMember.Id, ProjectRole.Member, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("User is already a member of this project.", result.Errors);
    }

    [Fact]
    public async Task SendInvitation_DuplicatePending_Fails()
    {
        var owner = CreateUser("owner6@test.com", "Owner");
        var invitee = CreateUser("invitee6@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("User already has a pending invitation to this project.", result.Errors);
    }

    [Fact]
    public async Task SendInvitation_AsOwnerRole_Fails()
    {
        var owner = CreateUser("owner7@test.com", "Owner");
        var invitee = CreateUser("invitee7@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Owner, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Cannot invite a user as Owner.", result.Errors);
    }

    [Fact]
    public async Task SendInvitation_InvalidProject_Fails()
    {
        var owner = CreateUser("owner8@test.com", "Owner");
        var invitee = CreateUser("invitee8@test.com", "Invitee");

        var result = await _invitationService.SendInvitation(999, invitee.Id, ProjectRole.Member, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Project not found.", result.Errors);
    }

    [Fact]
    public async Task SendInvitation_InvalidUser_Fails()
    {
        var owner = CreateUser("owner9@test.com", "Owner");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendInvitation(project.Id, "nonexistent-id", ProjectRole.Member, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Invited user not found.", result.Errors);
    }

    [Fact]
    public async Task SendInvitation_EmptyUserId_Fails()
    {
        var result = await _invitationService.SendInvitation(1, "", ProjectRole.Member, "inviter-id");

        Assert.False(result.Succeeded);
        Assert.Contains("Invited user ID is required.", result.Errors);
    }

    [Fact]
    public async Task SendInvitation_EmptyInviterId_Fails()
    {
        var result = await _invitationService.SendInvitation(1, "user-id", ProjectRole.Member, "");

        Assert.False(result.Succeeded);
        Assert.Contains("Inviter user ID is required.", result.Errors);
    }

    [Fact]
    public async Task SendInvitation_CreatesNotification()
    {
        var owner = CreateUser("notif-owner@test.com", "Owner");
        var invitee = CreateUser("notif-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var notifications = await _notificationService.GetUnreadForUser(invitee.Id);
        Assert.Single(notifications);
        Assert.Equal(NotificationType.ProjectInvitation, notifications[0].Type);
        Assert.Contains(project.Name, notifications[0].Message);
        Assert.Contains("Owner", notifications[0].Message);
    }

    [Fact]
    public async Task SendInvitation_NotificationReferencesInvitationId()
    {
        var owner = CreateUser("ref-owner@test.com", "Owner");
        var invitee = CreateUser("ref-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var notifications = await _notificationService.GetUnreadForUser(invitee.Id);
        Assert.Single(notifications);
        Assert.Equal(result.Data!.Id, notifications[0].ReferenceId);
    }

    [Fact]
    public async Task SendInvitation_WithAdminRole_SetsRole()
    {
        var owner = CreateUser("role-owner@test.com", "Owner");
        var invitee = CreateUser("role-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Admin, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(ProjectRole.Admin, result.Data!.Role);
    }

    // --- SendBulkInvitations ---

    [Fact]
    public async Task SendBulkInvitations_MultipleUsers_Succeeds()
    {
        var owner = CreateUser("bulk-owner@test.com", "Owner");
        var invitee1 = CreateUser("bulk1@test.com", "Bulk1");
        var invitee2 = CreateUser("bulk2@test.com", "Bulk2");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendBulkInvitations(
            project.Id,
            [invitee1.Id, invitee2.Id],
            ProjectRole.Member,
            owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count);
    }

    [Fact]
    public async Task SendBulkInvitations_EmptyList_Fails()
    {
        var result = await _invitationService.SendBulkInvitations(
            1,
            [],
            ProjectRole.Member,
            "owner-id");

        Assert.False(result.Succeeded);
        Assert.Contains("At least one user must be specified.", result.Errors);
    }

    [Fact]
    public async Task SendBulkInvitations_PartialFailure_ReturnsSuccessful()
    {
        var owner = CreateUser("partial-owner@test.com", "Owner");
        var invitee = CreateUser("partial-invitee@test.com", "Partial Invitee");
        var project = await CreateProjectWithOwner(owner);

        // One valid user, one invalid
        var result = await _invitationService.SendBulkInvitations(
            project.Id,
            [invitee.Id, "nonexistent-id"],
            ProjectRole.Member,
            owner.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
    }

    [Fact]
    public async Task SendBulkInvitations_AllFail_ReturnsFailure()
    {
        var owner = CreateUser("allfail-owner@test.com", "Owner");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendBulkInvitations(
            project.Id,
            ["bad-id-1", "bad-id-2"],
            ProjectRole.Member,
            owner.Id);

        Assert.False(result.Succeeded);
    }

    // --- AcceptInvitation ---

    [Fact]
    public async Task AcceptInvitation_ValidInvitation_CreatesProjectMember()
    {
        var owner = CreateUser("accept-owner@test.com", "Owner");
        var invitee = CreateUser("accept-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        var result = await _invitationService.AcceptInvitation(sendResult.Data!.Id, invitee.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(project.Id, result.Data.ProjectId);
        Assert.Equal(invitee.Id, result.Data.UserId);
        Assert.Equal(ProjectRole.Member, result.Data.Role);
    }

    [Fact]
    public async Task AcceptInvitation_UpdatesInvitationStatus()
    {
        var owner = CreateUser("accept2-owner@test.com", "Owner");
        var invitee = CreateUser("accept2-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        await _invitationService.AcceptInvitation(sendResult.Data!.Id, invitee.Id);

        var invitation = await _context.ProjectInvitations.FindAsync(sendResult.Data.Id);
        Assert.Equal(InvitationStatus.Accepted, invitation!.Status);
        Assert.NotNull(invitation.RespondedAt);
    }

    [Fact]
    public async Task AcceptInvitation_WithAdminRole_CreatesAdminMember()
    {
        var owner = CreateUser("accept3-owner@test.com", "Owner");
        var invitee = CreateUser("accept3-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Admin, owner.Id);
        var result = await _invitationService.AcceptInvitation(sendResult.Data!.Id, invitee.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(ProjectRole.Admin, result.Data!.Role);
    }

    [Fact]
    public async Task AcceptInvitation_WrongUser_Fails()
    {
        var owner = CreateUser("wrong-owner@test.com", "Owner");
        var invitee = CreateUser("wrong-invitee@test.com", "Invitee");
        var stranger = CreateUser("wrong-stranger@test.com", "Stranger");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        var result = await _invitationService.AcceptInvitation(sendResult.Data!.Id, stranger.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("You can only respond to your own invitations.", result.Errors);
    }

    [Fact]
    public async Task AcceptInvitation_AlreadyAccepted_Fails()
    {
        var owner = CreateUser("dbl-owner@test.com", "Owner");
        var invitee = CreateUser("dbl-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        await _invitationService.AcceptInvitation(sendResult.Data!.Id, invitee.Id);
        var result = await _invitationService.AcceptInvitation(sendResult.Data.Id, invitee.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("This invitation has already been responded to.", result.Errors);
    }

    [Fact]
    public async Task AcceptInvitation_InvalidId_Fails()
    {
        var result = await _invitationService.AcceptInvitation(999, "some-user-id");

        Assert.False(result.Succeeded);
        Assert.Contains("Invitation not found.", result.Errors);
    }

    [Fact]
    public async Task AcceptInvitation_EmptyUserId_Fails()
    {
        var result = await _invitationService.AcceptInvitation(1, "");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    // --- DeclineInvitation ---

    [Fact]
    public async Task DeclineInvitation_ValidInvitation_UpdatesStatus()
    {
        var owner = CreateUser("decline-owner@test.com", "Owner");
        var invitee = CreateUser("decline-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        var result = await _invitationService.DeclineInvitation(sendResult.Data!.Id, invitee.Id);

        Assert.True(result.Succeeded);

        var invitation = await _context.ProjectInvitations.FindAsync(sendResult.Data.Id);
        Assert.Equal(InvitationStatus.Declined, invitation!.Status);
        Assert.NotNull(invitation.RespondedAt);
    }

    [Fact]
    public async Task DeclineInvitation_DoesNotCreateMember()
    {
        var owner = CreateUser("decline2-owner@test.com", "Owner");
        var invitee = CreateUser("decline2-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        await _invitationService.DeclineInvitation(sendResult.Data!.Id, invitee.Id);

        var isMember = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == project.Id && pm.UserId == invitee.Id);
        Assert.False(isMember);
    }

    [Fact]
    public async Task DeclineInvitation_WrongUser_Fails()
    {
        var owner = CreateUser("decline3-owner@test.com", "Owner");
        var invitee = CreateUser("decline3-invitee@test.com", "Invitee");
        var stranger = CreateUser("decline3-stranger@test.com", "Stranger");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        var result = await _invitationService.DeclineInvitation(sendResult.Data!.Id, stranger.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("You can only respond to your own invitations.", result.Errors);
    }

    [Fact]
    public async Task DeclineInvitation_AlreadyDeclined_Fails()
    {
        var owner = CreateUser("decline4-owner@test.com", "Owner");
        var invitee = CreateUser("decline4-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var sendResult = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        await _invitationService.DeclineInvitation(sendResult.Data!.Id, invitee.Id);
        var result = await _invitationService.DeclineInvitation(sendResult.Data.Id, invitee.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("This invitation has already been responded to.", result.Errors);
    }

    [Fact]
    public async Task DeclineInvitation_EmptyUserId_Fails()
    {
        var result = await _invitationService.DeclineInvitation(1, "");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    // --- GetPendingInvitationsForProject ---

    [Fact]
    public async Task GetPendingInvitationsForProject_AsOwner_ReturnsInvitations()
    {
        var owner = CreateUser("proj-owner@test.com", "Owner");
        var invitee1 = CreateUser("proj-inv1@test.com", "Invitee1");
        var invitee2 = CreateUser("proj-inv2@test.com", "Invitee2");
        var project = await CreateProjectWithOwner(owner);

        await _invitationService.SendInvitation(project.Id, invitee1.Id, ProjectRole.Member, owner.Id);
        await _invitationService.SendInvitation(project.Id, invitee2.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.GetPendingInvitationsForProject(project.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetPendingInvitationsForProject_AsAdmin_Succeeds()
    {
        var owner = CreateUser("projadm-owner@test.com", "Owner");
        var admin = CreateUser("projadm-admin@test.com", "Admin");
        var invitee = CreateUser("projadm-inv@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        AddMember(project.Id, admin.Id, ProjectRole.Admin);

        await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.GetPendingInvitationsForProject(project.Id, admin.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
    }

    [Fact]
    public async Task GetPendingInvitationsForProject_AsMember_Fails()
    {
        var owner = CreateUser("projmem-owner@test.com", "Owner");
        var member = CreateUser("projmem-member@test.com", "Member");
        var project = await CreateProjectWithOwner(owner);
        AddMember(project.Id, member.Id, ProjectRole.Member);

        var result = await _invitationService.GetPendingInvitationsForProject(project.Id, member.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Only project owners and admins can view pending invitations.", result.Errors);
    }

    [Fact]
    public async Task GetPendingInvitationsForProject_OnlyReturnsPending()
    {
        var owner = CreateUser("pending-owner@test.com", "Owner");
        var invitee1 = CreateUser("pending-inv1@test.com", "Invitee1");
        var invitee2 = CreateUser("pending-inv2@test.com", "Invitee2");
        var project = await CreateProjectWithOwner(owner);

        var send1 = await _invitationService.SendInvitation(project.Id, invitee1.Id, ProjectRole.Member, owner.Id);
        await _invitationService.SendInvitation(project.Id, invitee2.Id, ProjectRole.Member, owner.Id);

        // Accept the first invitation
        await _invitationService.AcceptInvitation(send1.Data!.Id, invitee1.Id);

        var result = await _invitationService.GetPendingInvitationsForProject(project.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal(invitee2.Id, result.Data[0].InvitedUserId);
    }

    [Fact]
    public async Task GetPendingInvitationsForProject_IncludesUserDetails()
    {
        var owner = CreateUser("detail-owner@test.com", "Detail Owner");
        var invitee = CreateUser("detail-inv@test.com", "Detail Invitee");
        var project = await CreateProjectWithOwner(owner);

        await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.GetPendingInvitationsForProject(project.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Detail Invitee", result.Data![0].InvitedUser.DisplayName);
        Assert.Equal("Detail Owner", result.Data[0].InvitedByUser.DisplayName);
    }

    // --- GetPendingInvitationsForUser ---

    [Fact]
    public async Task GetPendingInvitationsForUser_ReturnsUserInvitations()
    {
        var owner1 = CreateUser("user-owner1@test.com", "Owner1");
        var owner2 = CreateUser("user-owner2@test.com", "Owner2");
        var invitee = CreateUser("user-invitee@test.com", "Invitee");
        var project1 = await CreateProjectWithOwner(owner1, "Project 1");
        var project2 = await CreateProjectWithOwner(owner2, "Project 2");

        await _invitationService.SendInvitation(project1.Id, invitee.Id, ProjectRole.Member, owner1.Id);
        await _invitationService.SendInvitation(project2.Id, invitee.Id, ProjectRole.Member, owner2.Id);

        var result = await _invitationService.GetPendingInvitationsForUser(invitee.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetPendingInvitationsForUser_OnlyReturnsPending()
    {
        var owner = CreateUser("userpend-owner@test.com", "Owner");
        var invitee = CreateUser("userpend-invitee@test.com", "Invitee");
        var project1 = await CreateProjectWithOwner(owner, "User Pending Project 1");
        var project2 = await CreateProjectWithOwner(owner, "User Pending Project 2");

        var send1 = await _invitationService.SendInvitation(project1.Id, invitee.Id, ProjectRole.Member, owner.Id);
        await _invitationService.SendInvitation(project2.Id, invitee.Id, ProjectRole.Member, owner.Id);

        await _invitationService.DeclineInvitation(send1.Data!.Id, invitee.Id);

        var result = await _invitationService.GetPendingInvitationsForUser(invitee.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
    }

    [Fact]
    public async Task GetPendingInvitationsForUser_IncludesProjectDetails()
    {
        var owner = CreateUser("userdet-owner@test.com", "Owner");
        var invitee = CreateUser("userdet-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner, "Detail Project");

        await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.GetPendingInvitationsForUser(invitee.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Detail Project", result.Data![0].Project.Name);
        Assert.Equal("Owner", result.Data[0].InvitedByUser.DisplayName);
    }

    [Fact]
    public async Task GetPendingInvitationsForUser_EmptyUserId_Fails()
    {
        var result = await _invitationService.GetPendingInvitationsForUser("");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    // --- Re-invite after decline ---

    [Fact]
    public async Task SendInvitation_AfterDecline_Succeeds()
    {
        var owner = CreateUser("reinvite-owner@test.com", "Owner");
        var invitee = CreateUser("reinvite-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var send1 = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        await _invitationService.DeclineInvitation(send1.Data!.Id, invitee.Id);

        // Should be able to re-invite after decline since there's no pending invitation
        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        Assert.True(result.Succeeded);
    }

    // --- Agent auto-accept ---

    private ApplicationUser CreateAgentUser(string email, string displayName)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            IsAgent = true
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        return user;
    }

    [Fact]
    public async Task SendInvitation_AgentUser_AutoAcceptsInvitation()
    {
        var owner = CreateUser("agent-owner@test.com", "Owner");
        var agent = CreateAgentUser("agent@test.com", "Agent Bot");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendInvitation(project.Id, agent.Id, ProjectRole.Member, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(InvitationStatus.Accepted, result.Data!.Status);
        Assert.NotNull(result.Data.RespondedAt);
    }

    [Fact]
    public async Task SendInvitation_AgentUser_CreatesProjectMember()
    {
        var owner = CreateUser("agent-member-owner@test.com", "Owner");
        var agent = CreateAgentUser("agent-member@test.com", "Agent Bot");
        var project = await CreateProjectWithOwner(owner);

        await _invitationService.SendInvitation(project.Id, agent.Id, ProjectRole.Member, owner.Id);

        var isMember = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == project.Id && pm.UserId == agent.Id);
        Assert.True(isMember);
    }

    [Fact]
    public async Task SendInvitation_AgentUser_SetsCorrectRole()
    {
        var owner = CreateUser("agent-role-owner@test.com", "Owner");
        var agent = CreateAgentUser("agent-role@test.com", "Agent Bot");
        var project = await CreateProjectWithOwner(owner);

        await _invitationService.SendInvitation(project.Id, agent.Id, ProjectRole.Admin, owner.Id);

        var member = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == project.Id && pm.UserId == agent.Id);
        Assert.NotNull(member);
        Assert.Equal(ProjectRole.Admin, member.Role);
    }

    [Fact]
    public async Task SendInvitation_AgentUser_DoesNotCreateNotification()
    {
        var owner = CreateUser("agent-notif-owner@test.com", "Owner");
        var agent = CreateAgentUser("agent-notif@test.com", "Agent Bot");
        var project = await CreateProjectWithOwner(owner);

        await _invitationService.SendInvitation(project.Id, agent.Id, ProjectRole.Member, owner.Id);

        var notifications = await _context.Notifications
            .Where(n => n.UserId == agent.Id)
            .ToListAsync();
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task SendInvitation_NonAgentUser_StillCreatesPendingInvitation()
    {
        var owner = CreateUser("noagent-owner@test.com", "Owner");
        var invitee = CreateUser("noagent-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);

        var result = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(InvitationStatus.Pending, result.Data!.Status);
        Assert.Null(result.Data.RespondedAt);
    }

    // --- CancelInvitation ---

    [Fact]
    public async Task CancelInvitation_AsOwner_Succeeds()
    {
        var owner = CreateUser("cancel-owner@test.com", "Owner");
        var invitee = CreateUser("cancel-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.CancelInvitation(send.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CancelInvitation_SetsStatusToCancelled()
    {
        var owner = CreateUser("cancel-status-owner@test.com", "Owner");
        var invitee = CreateUser("cancel-status-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        await _invitationService.CancelInvitation(send.Data!.Id, owner.Id);

        var invitation = await _context.ProjectInvitations.FindAsync(send.Data.Id);
        Assert.Equal(InvitationStatus.Cancelled, invitation!.Status);
        Assert.NotNull(invitation.RespondedAt);
    }

    [Fact]
    public async Task CancelInvitation_AsAdmin_Succeeds()
    {
        var owner = CreateUser("cancel-admin-owner@test.com", "Owner");
        var admin = CreateUser("cancel-admin@test.com", "Admin");
        var invitee = CreateUser("cancel-admin-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        AddMember(project.Id, admin.Id, ProjectRole.Admin);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.CancelInvitation(send.Data!.Id, admin.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CancelInvitation_AsMember_Fails()
    {
        var owner = CreateUser("cancel-mem-owner@test.com", "Owner");
        var member = CreateUser("cancel-member@test.com", "Member");
        var invitee = CreateUser("cancel-mem-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        AddMember(project.Id, member.Id, ProjectRole.Member);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.CancelInvitation(send.Data!.Id, member.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Only project owners and admins can cancel invitations.", result.Errors);
    }

    [Fact]
    public async Task CancelInvitation_AlreadyAccepted_Fails()
    {
        var owner = CreateUser("cancel-acc-owner@test.com", "Owner");
        var invitee = CreateUser("cancel-acc-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        await _invitationService.AcceptInvitation(send.Data!.Id, invitee.Id);

        var result = await _invitationService.CancelInvitation(send.Data.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Only pending invitations can be cancelled.", result.Errors);
    }

    [Fact]
    public async Task CancelInvitation_InvalidId_Fails()
    {
        var owner = CreateUser("cancel-invalid-owner@test.com", "Owner");

        var result = await _invitationService.CancelInvitation(9999, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Invitation not found.", result.Errors);
    }

    [Fact]
    public async Task CancelInvitation_EmptyUserId_Fails()
    {
        var result = await _invitationService.CancelInvitation(1, "");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    // --- ResendInvitation ---

    [Fact]
    public async Task ResendInvitation_AsOwner_Succeeds()
    {
        var owner = CreateUser("resend-owner@test.com", "Owner");
        var invitee = CreateUser("resend-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.ResendInvitation(send.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ResendInvitation_CreatesNotificationForNonAgent()
    {
        var owner = CreateUser("resend-notif-owner@test.com", "Owner");
        var invitee = CreateUser("resend-notif-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        // Clear existing notifications
        var existingNotifications = _context.Notifications.Where(n => n.UserId == invitee.Id);
        _context.Notifications.RemoveRange(existingNotifications);
        await _context.SaveChangesAsync();

        await _invitationService.ResendInvitation(send.Data!.Id, owner.Id);

        var notifications = await _context.Notifications
            .Where(n => n.UserId == invitee.Id)
            .ToListAsync();
        Assert.Single(notifications);
        Assert.Contains("reminded you", notifications[0].Message);
    }

    [Fact]
    public async Task ResendInvitation_AgentUser_AutoAccepts()
    {
        var owner = CreateUser("resend-agent-owner@test.com", "Owner");
        var agent = CreateAgentUser("resend-agent@test.com", "Agent Bot");
        var project = await CreateProjectWithOwner(owner);

        // Agent invitations are auto-accepted, so we need to set up a pending invitation manually
        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = agent.Id,
            InvitedByUserId = owner.Id,
            Status = InvitationStatus.Pending,
            Role = ProjectRole.Member,
            CreatedAt = DateTime.UtcNow
        };
        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        var result = await _invitationService.ResendInvitation(invitation.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(InvitationStatus.Accepted, result.Data!.Status);
    }

    [Fact]
    public async Task ResendInvitation_AgentUser_CreatesProjectMember()
    {
        var owner = CreateUser("resend-agentmem-owner@test.com", "Owner");
        var agent = CreateAgentUser("resend-agentmem@test.com", "Agent Bot");
        var project = await CreateProjectWithOwner(owner);

        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = agent.Id,
            InvitedByUserId = owner.Id,
            Status = InvitationStatus.Pending,
            Role = ProjectRole.Member,
            CreatedAt = DateTime.UtcNow
        };
        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        await _invitationService.ResendInvitation(invitation.Id, owner.Id);

        var isMember = await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == project.Id && pm.UserId == agent.Id);
        Assert.True(isMember);
    }

    [Fact]
    public async Task ResendInvitation_AgentUser_DoesNotCreateNotification()
    {
        var owner = CreateUser("resend-agentnotif-owner@test.com", "Owner");
        var agent = CreateAgentUser("resend-agentnotif@test.com", "Agent Bot");
        var project = await CreateProjectWithOwner(owner);

        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = agent.Id,
            InvitedByUserId = owner.Id,
            Status = InvitationStatus.Pending,
            Role = ProjectRole.Member,
            CreatedAt = DateTime.UtcNow
        };
        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        await _invitationService.ResendInvitation(invitation.Id, owner.Id);

        var notifications = await _context.Notifications
            .Where(n => n.UserId == agent.Id)
            .ToListAsync();
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task ResendInvitation_AsMember_Fails()
    {
        var owner = CreateUser("resend-mem-owner@test.com", "Owner");
        var member = CreateUser("resend-member@test.com", "Member");
        var invitee = CreateUser("resend-mem-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        AddMember(project.Id, member.Id, ProjectRole.Member);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);

        var result = await _invitationService.ResendInvitation(send.Data!.Id, member.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Only project owners and admins can resend invitations.", result.Errors);
    }

    [Fact]
    public async Task ResendInvitation_AlreadyAccepted_Fails()
    {
        var owner = CreateUser("resend-acc-owner@test.com", "Owner");
        var invitee = CreateUser("resend-acc-invitee@test.com", "Invitee");
        var project = await CreateProjectWithOwner(owner);
        var send = await _invitationService.SendInvitation(project.Id, invitee.Id, ProjectRole.Member, owner.Id);
        await _invitationService.AcceptInvitation(send.Data!.Id, invitee.Id);

        var result = await _invitationService.ResendInvitation(send.Data.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Only pending invitations can be resent.", result.Errors);
    }

    [Fact]
    public async Task ResendInvitation_InvalidId_Fails()
    {
        var owner = CreateUser("resend-invalid-owner@test.com", "Owner");

        var result = await _invitationService.ResendInvitation(9999, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Invitation not found.", result.Errors);
    }

    [Fact]
    public async Task ResendInvitation_EmptyUserId_Fails()
    {
        var result = await _invitationService.ResendInvitation(1, "");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
