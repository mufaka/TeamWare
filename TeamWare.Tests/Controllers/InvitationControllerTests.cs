using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class InvitationControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InvitationControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(
        string email, string displayName = "Test User")
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            return (existing.Id, await GetLoginCookie(email));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };

        await userManager.CreateAsync(user, "TestPass1!");
        await userManager.AddToRoleAsync(user, SeedData.UserRoleName);

        var cookie = await GetLoginCookie(email);
        return (user.Id, cookie);
    }

    private async Task<string> GetLoginCookie(string email)
    {
        var getResponse = await _client.GetAsync("/Account/Login");
        var getContent = await getResponse.Content.ReadAsStringAsync();

        var token = ExtractAntiForgeryToken(getContent);
        var cookies = getResponse.Headers.GetValues("Set-Cookie");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = "TestPass1!",
            ["__RequestVerificationToken"] = token
        });

        var loginResponse = await _client.SendAsync(request);
        var loginCookies = loginResponse.Headers.GetValues("Set-Cookie");

        return string.Join("; ", loginCookies);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var tokenStart = html.IndexOf("name=\"__RequestVerificationToken\"", StringComparison.Ordinal);
        if (tokenStart == -1) return string.Empty;

        var valueStart = html.IndexOf("value=\"", tokenStart, StringComparison.Ordinal) + 7;
        var valueEnd = html.IndexOf("\"", valueStart, StringComparison.Ordinal);
        return html[valueStart..valueEnd];
    }

    private async Task<int> CreateProjectWithOwner(string ownerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project
        {
            Name = $"InvTest Project {Guid.NewGuid():N}",
            Description = "Test project for invitations",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var member = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = ownerUserId,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        };
        context.ProjectMembers.Add(member);
        await context.SaveChangesAsync();

        return project.Id;
    }

    private async Task<int> CreateInvitation(int projectId, string invitedUserId, string invitedByUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var invitation = new ProjectInvitation
        {
            ProjectId = projectId,
            InvitedUserId = invitedUserId,
            InvitedByUserId = invitedByUserId,
            Status = InvitationStatus.Pending,
            Role = ProjectRole.Member,
            CreatedAt = DateTime.UtcNow
        };
        context.ProjectInvitations.Add(invitation);
        await context.SaveChangesAsync();

        return invitation.Id;
    }

    private async Task<(string Token, string Cookies)> GetAntiForgeryFromPage(string url, string authCookie)
    {
        var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
        getRequest.Headers.Add("Cookie", authCookie);
        var getResponse = await _client.SendAsync(getRequest);
        var html = await getResponse.Content.ReadAsStringAsync();

        var token = ExtractAntiForgeryToken(html);

        var allCookies = new List<string> { authCookie };
        if (getResponse.Headers.TryGetValues("Set-Cookie", out var responseCookies))
        {
            allCookies.AddRange(responseCookies);
        }

        return (token, string.Join("; ", allCookies));
    }

    // --- Unauthenticated ---

    [Fact]
    public async Task PendingForUser_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Invitation/PendingForUser");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Accept_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.PostAsync("/Invitation/Accept/1", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Decline_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.PostAsync("/Invitation/Decline/1", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // --- PendingForUser ---

    [Fact]
    public async Task PendingForUser_Authenticated_ReturnsSuccess()
    {
        var (_, cookie) = await CreateAndLoginUser("inv-pending@test.com", "Inv Pending User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Invitation/PendingForUser");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PendingForUser_NoPending_ShowsEmptyMessage()
    {
        var (_, cookie) = await CreateAndLoginUser("inv-empty@test.com", "Inv Empty User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Invitation/PendingForUser");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("No pending invitations", html);
    }

    [Fact]
    public async Task PendingForUser_WithPending_ShowsInvitation()
    {
        var (ownerId, _) = await CreateAndLoginUser("inv-owner-pfu@test.com", "PFU Owner");
        var (inviteeId, inviteeCookie) = await CreateAndLoginUser("inv-invitee-pfu@test.com", "PFU Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);
        await CreateInvitation(projectId, inviteeId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Invitation/PendingForUser");
        request.Headers.Add("Cookie", inviteeCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PFU Owner", html);
        Assert.Contains("Accept", html);
        Assert.Contains("Decline", html);
    }

    // --- Accept ---

    [Fact]
    public async Task Accept_ValidInvitation_RedirectsToProject()
    {
        var (ownerId, _) = await CreateAndLoginUser("inv-owner-acc@test.com", "Acc Owner");
        var (inviteeId, inviteeCookie) = await CreateAndLoginUser("inv-invitee-acc@test.com", "Acc Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);
        var invitationId = await CreateInvitation(projectId, inviteeId, ownerId);

        var (token, cookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", inviteeCookie);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, $"/Invitation/Accept/{invitationId}");
        postRequest.Headers.Add("Cookie", cookies);
        postRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Project/Details/{projectId}", response.Headers.Location?.ToString());

        // Verify the user is now a member
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var membership = await context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == inviteeId);
        Assert.True(membership);
    }

    [Fact]
    public async Task Accept_AlreadyRespondedInvitation_RedirectsWithError()
    {
        var (ownerId, _) = await CreateAndLoginUser("inv-owner-arr@test.com", "Arr Owner");
        var (inviteeId, inviteeCookie) = await CreateAndLoginUser("inv-invitee-arr@test.com", "Arr Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);
        var invitationId = await CreateInvitation(projectId, inviteeId, ownerId);

        // Decline first
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var inv = await context.ProjectInvitations.FindAsync(invitationId);
            inv!.Status = InvitationStatus.Declined;
            inv.RespondedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        var (token, cookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", inviteeCookie);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, $"/Invitation/Accept/{invitationId}");
        postRequest.Headers.Add("Cookie", cookies);
        postRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(postRequest);

        // Should redirect to PendingForUser (error case)
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Invitation/PendingForUser", response.Headers.Location?.ToString());
    }

    // --- Decline ---

    [Fact]
    public async Task Decline_ValidInvitation_RedirectsToPending()
    {
        var (ownerId, _) = await CreateAndLoginUser("inv-owner-dec@test.com", "Dec Owner");
        var (inviteeId, inviteeCookie) = await CreateAndLoginUser("inv-invitee-dec@test.com", "Dec Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);
        var invitationId = await CreateInvitation(projectId, inviteeId, ownerId);

        var (token, cookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", inviteeCookie);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, $"/Invitation/Decline/{invitationId}");
        postRequest.Headers.Add("Cookie", cookies);
        postRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Invitation/PendingForUser", response.Headers.Location?.ToString());

        // Verify the invitation status
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitation = await context.ProjectInvitations.FindAsync(invitationId);
        Assert.Equal(InvitationStatus.Declined, invitation!.Status);
    }

    // --- Send (via InvitationController) ---

    [Fact]
    public async Task Send_AsOwner_RedirectsToProject()
    {
        var (ownerId, ownerCookie) = await CreateAndLoginUser("inv-owner-send@test.com", "Send Owner");
        var (inviteeId, _) = await CreateAndLoginUser("inv-invitee-send@test.com", "Send Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);

        var (token, cookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", ownerCookie);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/Invitation/Send");
        postRequest.Headers.Add("Cookie", cookies);
        postRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectId"] = projectId.ToString(),
            ["UserId"] = inviteeId,
            ["Role"] = "Member",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Project/Details/{projectId}", response.Headers.Location?.ToString());

        // Verify invitation was created
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitation = await context.ProjectInvitations
            .AnyAsync(i => i.ProjectId == projectId && i.InvitedUserId == inviteeId && i.Status == InvitationStatus.Pending);
        Assert.True(invitation);
    }

    [Fact]
    public async Task Send_AsNonMember_RedirectsWithError()
    {
        var (ownerId, _) = await CreateAndLoginUser("inv-owner-snm@test.com", "SNM Owner");
        var (nonMemberId, nonMemberCookie) = await CreateAndLoginUser("inv-nonmember-snm@test.com", "SNM NonMember");
        var (inviteeId, _) = await CreateAndLoginUser("inv-invitee-snm@test.com", "SNM Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);

        // Get antiforgery from any accessible page
        var (token, cookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", nonMemberCookie);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/Invitation/Send");
        postRequest.Headers.Add("Cookie", cookies);
        postRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectId"] = projectId.ToString(),
            ["UserId"] = inviteeId,
            ["Role"] = "Member",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(postRequest);

        // Should redirect back to project details
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // --- PendingForProject ---

    [Fact]
    public async Task PendingForProject_AsOwner_ReturnsSuccess()
    {
        var (ownerId, ownerCookie) = await CreateAndLoginUser("inv-owner-pfp@test.com", "PFP Owner");
        var (inviteeId, _) = await CreateAndLoginUser("inv-invitee-pfp@test.com", "PFP Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);
        await CreateInvitation(projectId, inviteeId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Invitation/PendingForProject?projectId={projectId}");
        request.Headers.Add("Cookie", ownerCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PFP Invitee", html);
        Assert.Contains("Pending", html);
    }

    [Fact]
    public async Task PendingForProject_AsNonMember_RedirectsToProject()
    {
        var (ownerId, _) = await CreateAndLoginUser("inv-owner-pfpnm@test.com", "PFP NM Owner");
        var (nonMemberId, nonMemberCookie) = await CreateAndLoginUser("inv-nonmember-pfpnm@test.com", "PFP NM NonMember");

        var projectId = await CreateProjectWithOwner(ownerId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Invitation/PendingForProject?projectId={projectId}");
        request.Headers.Add("Cookie", nonMemberCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Project/Details/{projectId}", response.Headers.Location?.ToString());
    }

    // --- Project Details shows invitation UI ---

    [Fact]
    public async Task ProjectDetails_AsOwner_ShowsInviteButton()
    {
        var (ownerId, ownerCookie) = await CreateAndLoginUser("inv-owner-details@test.com", "Details Owner");
        var projectId = await CreateProjectWithOwner(ownerId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Project/Details/{projectId}");
        request.Headers.Add("Cookie", ownerCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invite", html);
        Assert.Contains("Send Invitation", html);
        Assert.Contains("Search Users", html);
    }

    [Fact]
    public async Task ProjectDetails_WithPendingInvitations_ShowsPendingSection()
    {
        var (ownerId, ownerCookie) = await CreateAndLoginUser("inv-owner-detpend@test.com", "DetPend Owner");
        var (inviteeId, _) = await CreateAndLoginUser("inv-invitee-detpend@test.com", "DetPend Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);
        await CreateInvitation(projectId, inviteeId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Project/Details/{projectId}");
        request.Headers.Add("Cookie", ownerCookie);

        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Pending Invitations", html);
        Assert.Contains("DetPend Invitee", html);
    }

    // --- InviteMember on ProjectController (replaced flow) ---

    [Fact]
    public async Task ProjectController_InviteMember_SendsInvitationInsteadOfDirectAdd()
    {
        var (ownerId, ownerCookie) = await CreateAndLoginUser("inv-owner-pcim@test.com", "PCIM Owner");
        var (inviteeId, _) = await CreateAndLoginUser("inv-invitee-pcim@test.com", "PCIM Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);

        var (token, cookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", ownerCookie);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/Project/InviteMember");
        postRequest.Headers.Add("Cookie", cookies);
        postRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectId"] = projectId.ToString(),
            ["UserId"] = inviteeId,
            ["Role"] = "Member",
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Verify: invitation was created, NOT a direct membership
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var hasInvitation = await context.ProjectInvitations
            .AnyAsync(i => i.ProjectId == projectId && i.InvitedUserId == inviteeId && i.Status == InvitationStatus.Pending);
        Assert.True(hasInvitation);

        var hasDirectMembership = await context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == inviteeId);
        Assert.False(hasDirectMembership);
    }

    // --- Full Workflow ---

    [Fact]
    public async Task FullWorkflow_Invite_Accept_BecomesMember()
    {
        var (ownerId, ownerCookie) = await CreateAndLoginUser("inv-owner-fw@test.com", "FW Owner");
        var (inviteeId, inviteeCookie) = await CreateAndLoginUser("inv-invitee-fw@test.com", "FW Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);

        // Step 1: Owner sends invitation
        var (sendToken, sendCookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", ownerCookie);

        var sendRequest = new HttpRequestMessage(HttpMethod.Post, "/Invitation/Send");
        sendRequest.Headers.Add("Cookie", sendCookies);
        sendRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectId"] = projectId.ToString(),
            ["UserId"] = inviteeId,
            ["Role"] = "Member",
            ["__RequestVerificationToken"] = sendToken
        });

        var sendResponse = await _client.SendAsync(sendRequest);
        Assert.Equal(HttpStatusCode.Redirect, sendResponse.StatusCode);

        // Step 2: Invitee sees the invitation
        var pendingRequest = new HttpRequestMessage(HttpMethod.Get, "/Invitation/PendingForUser");
        pendingRequest.Headers.Add("Cookie", inviteeCookie);

        var pendingResponse = await _client.SendAsync(pendingRequest);
        var pendingHtml = await pendingResponse.Content.ReadAsStringAsync();
        Assert.Contains("FW Owner", pendingHtml);
        Assert.Contains("Accept", pendingHtml);

        // Step 3: Invitee accepts the invitation
        int invitationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var invitation = await context.ProjectInvitations
                .FirstAsync(i => i.ProjectId == projectId && i.InvitedUserId == inviteeId && i.Status == InvitationStatus.Pending);
            invitationId = invitation.Id;
        }

        var (acceptToken, acceptCookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", inviteeCookie);

        var acceptRequest = new HttpRequestMessage(HttpMethod.Post, $"/Invitation/Accept/{invitationId}");
        acceptRequest.Headers.Add("Cookie", acceptCookies);
        acceptRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = acceptToken
        });

        var acceptResponse = await _client.SendAsync(acceptRequest);
        Assert.Equal(HttpStatusCode.Redirect, acceptResponse.StatusCode);
        Assert.Contains($"/Project/Details/{projectId}", acceptResponse.Headers.Location?.ToString());

        // Step 4: Verify membership
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var isMember = await context.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == inviteeId);
            Assert.True(isMember);
        }
    }

    // --- Notification view shows invitation link ---

    [Fact]
    public async Task NotificationIndex_ProjectInvitationNotification_ShowsViewInvitationLink()
    {
        var (ownerId, ownerCookie) = await CreateAndLoginUser("inv-owner-notif@test.com", "Notif Owner");
        var (inviteeId, inviteeCookie) = await CreateAndLoginUser("inv-invitee-notif@test.com", "Notif Invitee");

        var projectId = await CreateProjectWithOwner(ownerId);

        // Send invitation (which triggers a notification)
        var (sendToken, sendCookies) = await GetAntiForgeryFromPage("/Invitation/PendingForUser", ownerCookie);

        var sendRequest = new HttpRequestMessage(HttpMethod.Post, "/Invitation/Send");
        sendRequest.Headers.Add("Cookie", sendCookies);
        sendRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProjectId"] = projectId.ToString(),
            ["UserId"] = inviteeId,
            ["Role"] = "Member",
            ["__RequestVerificationToken"] = sendToken
        });

        await _client.SendAsync(sendRequest);

        // Check notification page
        var notifRequest = new HttpRequestMessage(HttpMethod.Get, "/Notification");
        notifRequest.Headers.Add("Cookie", inviteeCookie);

        var notifResponse = await _client.SendAsync(notifRequest);
        var html = await notifResponse.Content.ReadAsStringAsync();

        Assert.Contains("View Invitation", html);
        Assert.Contains("invited you to join project", html);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
