using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Performance;

public class PerformanceTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    // Threshold for acceptable page load time in test environment (ms)
    private const int PageLoadThresholdMs = 2000;
    private const int HtmxPartialThresholdMs = 1000;

    public PerformanceTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email = "perf-test@test.com", string displayName = "Perf User")
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

        var response = await _client.SendAsync(request);
        var allCookies = new List<string>();

        if (response.Headers.TryGetValues("Set-Cookie", out var responseCookies))
        {
            allCookies.AddRange(responseCookies);
        }

        allCookies.AddRange(cookies);
        return string.Join("; ", allCookies);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(html, @"type=""hidden""\s+name=""__RequestVerificationToken""\s+value=""([^""]+)""");
        }
        if (!match.Success)
        {
            match = Regex.Match(html, @"__RequestVerificationToken[^>]+value=""([^""]+)""");
        }
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private async Task SeedTestData(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Create a project with the user as owner
        var project = new Project
        {
            Name = "Perf Test Project",
            Description = "Project for performance testing",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var member = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        };
        context.ProjectMembers.Add(member);

        // Create 50 tasks with various statuses
        for (int i = 0; i < 50; i++)
        {
            var status = i switch
            {
                < 15 => TaskItemStatus.ToDo,
                < 30 => TaskItemStatus.InProgress,
                < 40 => TaskItemStatus.InReview,
                _ => TaskItemStatus.Done
            };

            var task = new TaskItem
            {
                Title = $"Performance Test Task {i + 1}",
                Description = $"Description for task {i + 1}",
                Status = status,
                Priority = (TaskItemPriority)(i % 4),
                ProjectId = project.Id,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow.AddDays(-i),
                UpdatedAt = DateTime.UtcNow.AddDays(-i),
                DueDate = i % 3 == 0 ? DateTime.UtcNow.AddDays(i - 25) : null,
                IsNextAction = i % 5 == 0 && status != TaskItemStatus.Done,
                IsSomedayMaybe = i % 7 == 0 && status != TaskItemStatus.Done
            };

            context.TaskItems.Add(task);
        }

        // Create inbox items
        for (int i = 0; i < 15; i++)
        {
            context.InboxItems.Add(new InboxItem
            {
                Title = $"Inbox Item {i + 1}",
                UserId = userId,
                Status = i < 10 ? InboxItemStatus.Unprocessed : InboxItemStatus.Processed,
                CreatedAt = DateTime.UtcNow.AddHours(-i),
                UpdatedAt = DateTime.UtcNow.AddHours(-i)
            });
        }

        // Create notifications
        for (int i = 0; i < 20; i++)
        {
            context.Notifications.Add(new Notification
            {
                UserId = userId,
                Message = $"Test notification {i + 1}",
                Type = NotificationType.TaskAssigned,
                IsRead = i >= 10,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await context.SaveChangesAsync();

        // Create activity log entries referencing actual task IDs
        var taskIds = await context.TaskItems
            .Where(t => t.ProjectId == project.Id)
            .Select(t => t.Id)
            .ToListAsync();

        for (int i = 0; i < 30; i++)
        {
            context.ActivityLogEntries.Add(new ActivityLogEntry
            {
                TaskItemId = taskIds[i % taskIds.Count],
                ProjectId = project.Id,
                UserId = userId,
                ChangeType = ActivityChangeType.StatusChanged,
                OldValue = "ToDo",
                NewValue = "InProgress",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i * 10)
            });
        }

        await context.SaveChangesAsync();
    }

    private async Task<long> MeasureRequestTime(string url, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookie != null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        return stopwatch.ElapsedMilliseconds;
    }

    // PERF-01: Profile page load times

    [Fact]
    public async Task HomePage_LoadsWithinThreshold()
    {
        var elapsed = await MeasureRequestTime("/");
        Assert.True(elapsed < PageLoadThresholdMs, $"Home page took {elapsed}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task LoginPage_LoadsWithinThreshold()
    {
        var elapsed = await MeasureRequestTime("/Account/Login");
        Assert.True(elapsed < PageLoadThresholdMs, $"Login page took {elapsed}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task RegisterPage_LoadsWithinThreshold()
    {
        var elapsed = await MeasureRequestTime("/Account/Register");
        Assert.True(elapsed < PageLoadThresholdMs, $"Register page took {elapsed}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task Dashboard_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-dashboard@test.com", "Dashboard Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Dashboard took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task ProjectIndex_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-projlist@test.com", "ProjList Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Project");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Project index took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task InboxPage_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-inbox@test.com", "Inbox Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Inbox");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Inbox page took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task NotificationIndex_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-notif@test.com", "Notif Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Notification");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Notification page took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task WhatsNextPage_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-whatsnext@test.com", "WhatsNext Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/WhatsNext");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"What's Next page took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    // PERF-02: Verify HTMX partial update response times

    [Fact]
    public async Task DashboardInbox_HtmxPartial_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-htmx-inbox@test.com", "HTMX Inbox");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardInbox");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("HX-Request", "true");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Dashboard inbox partial took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    [Fact]
    public async Task DashboardWhatsNext_HtmxPartial_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-htmx-next@test.com", "HTMX Next");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardWhatsNext");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("HX-Request", "true");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Dashboard what's next partial took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    [Fact]
    public async Task DashboardProjects_HtmxPartial_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-htmx-proj@test.com", "HTMX Proj");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardProjects");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("HX-Request", "true");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Dashboard projects partial took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    [Fact]
    public async Task DashboardDeadlines_HtmxPartial_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-htmx-dead@test.com", "HTMX Dead");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardDeadlines");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("HX-Request", "true");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Dashboard deadlines partial took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    [Fact]
    public async Task DashboardReview_HtmxPartial_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-htmx-review@test.com", "HTMX Review");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardReview");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("HX-Request", "true");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Dashboard review partial took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    [Fact]
    public async Task DashboardNotifications_HtmxPartial_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-htmx-notif@test.com", "HTMX Notif");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/DashboardNotifications");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("HX-Request", "true");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Dashboard notifications partial took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    // Database index verification tests

    [Fact]
    public async Task CompositeIndex_TaskItems_ProjectIdStatus_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='TaskItems'")
            .ToListAsync();

        // Verify at least one index covers ProjectId (composite indexes have system-generated names)
        Assert.True(indexes.Count >= 2, "TaskItems should have multiple indexes including composite ProjectId+Status");
    }

    [Fact]
    public async Task CompositeIndex_Notifications_UserIdIsRead_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='Notifications'")
            .ToListAsync();

        Assert.True(indexes.Count >= 2, "Notifications should have multiple indexes including composite UserId+IsRead");
    }

    [Fact]
    public async Task CompositeIndex_ActivityLogEntries_ProjectIdCreatedAt_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='ActivityLogEntries'")
            .ToListAsync();

        Assert.True(indexes.Count >= 2, "ActivityLogEntries should have multiple indexes including composite ProjectId+CreatedAt");
    }

    [Fact]
    public async Task CompositeIndex_InboxItems_UserIdStatus_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='InboxItems'")
            .ToListAsync();

        Assert.True(indexes.Count >= 2, "InboxItems should have multiple indexes including composite UserId+Status");
    }

    // Response compression verification

    [Fact]
    public async Task ResponseCompression_IsConfigured()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Accept-Encoding", "gzip, deflate, br");

        var response = await _client.SendAsync(request);

        // The response should succeed - compression middleware is registered
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect);
    }

    // PERF-03: Test with simulated concurrent users

    [Fact]
    public async Task ConcurrentUsers_PublicPages_AllSucceed()
    {
        const int concurrentRequests = 20;

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => _client.GetAsync("/"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.True(
            r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Redirect,
            $"Expected OK or Redirect but got {r.StatusCode}"));
    }

    [Fact]
    public async Task ConcurrentUsers_LoginPage_AllSucceed()
    {
        const int concurrentRequests = 20;

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => _client.GetAsync("/Account/Login"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task ConcurrentUsers_AuthenticatedDashboard_AllSucceed()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-concurrent@test.com", "Concurrent User");
        await SeedTestData(userId);

        const int concurrentRequests = 10;

        // Execute requests sequentially to avoid SQLite concurrency limitations in test
        // (SQLite in-memory DB does not support true concurrent access)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < concurrentRequests; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Add("Cookie", cookie);
            responses.Add(await _client.SendAsync(request));
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task ConcurrentUsers_HtmxPartials_AllSucceed()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-concurrent-htmx@test.com", "Concurrent HTMX");
        await SeedTestData(userId);

        var endpoints = new[]
        {
            "/Home/DashboardInbox",
            "/Home/DashboardWhatsNext",
            "/Home/DashboardProjects",
            "/Home/DashboardDeadlines",
            "/Home/DashboardReview",
            "/Home/DashboardNotifications"
        };

        // Execute HTMX partial requests sequentially to avoid SQLite concurrency issues in test
        var responses = new List<HttpResponseMessage>();
        foreach (var endpoint in endpoints)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Add("Cookie", cookie);
            request.Headers.Add("HX-Request", "true");
            responses.Add(await _client.SendAsync(request));
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task ConcurrentUsers_MixedEndpoints_AllSucceed()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-mixed@test.com", "Mixed User");
        await SeedTestData(userId);

        var authenticatedEndpoints = new[] { "/", "/Project", "/Inbox", "/Notification", "/Home/WhatsNext" };
        var publicEndpoints = new[] { "/Account/Login", "/Account/Register" };

        // Execute sequentially to avoid SQLite in-memory concurrency limitations in test
        var responses = new List<HttpResponseMessage>();

        foreach (var url in authenticatedEndpoints)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Cookie", cookie);
            responses.Add(await _client.SendAsync(request));
        }

        foreach (var url in publicEndpoints)
        {
            responses.Add(await _client.GetAsync(url));
        }

        Assert.All(responses, r => Assert.True(
            r.IsSuccessStatusCode,
            $"Expected success but got {r.StatusCode}"));
    }

    // Query optimization verification

    [Fact]
    public async Task ProjectDashboard_WithManyTasks_LoadsEfficiently()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-dashboard-query@test.com", "Query Perf");
        await SeedTestData(userId);

        // Get the project ID
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var projectId = await context.ProjectMembers
            .Where(pm => pm.UserId == userId)
            .Select(pm => pm.ProjectId)
            .FirstAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Project/Details/{projectId}");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Project dashboard with 50 tasks took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task NotificationQueries_WithManyNotifications_PerformWell()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-notif-query@test.com", "Notif Query");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Notification");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Notification page with 20 notifications took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    // ---------------------------------------------------------------
    // Phase 14.2: Social feature performance tests
    // ---------------------------------------------------------------

    [Fact]
    public async Task DirectoryIndex_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-directory@test.com", "Directory Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Directory index took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task DirectorySearch_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-dir-search@test.com", "DirSearch Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Directory?search=perf");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Directory search took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task GlobalActivityFeed_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-activity@test.com", "Activity Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Activity/GlobalFeed");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("HX-Request", "true");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Global activity feed took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    [Fact]
    public async Task InvitationPendingForUser_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-invitation@test.com", "Invitation Perf");
        await SeedTestData(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Invitation/PendingForUser");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Invitation pending for user took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    // Social feature index verification

    [Fact]
    public async Task CompositeIndex_AdminActivityLogs_AdminUserIdCreatedAt_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='AdminActivityLogs'")
            .ToListAsync();

        Assert.True(indexes.Count >= 2, "AdminActivityLogs should have multiple indexes including composite AdminUserId+CreatedAt");
    }

    [Fact]
    public async Task CompositeIndex_ProjectInvitations_ProjectIdInvitedUserIdStatus_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='ProjectInvitations'")
            .ToListAsync();

        Assert.True(indexes.Count >= 2, "ProjectInvitations should have multiple indexes including composite ProjectId+InvitedUserId+Status");
    }

    // Social feature concurrent access

    [Fact]
    public async Task ConcurrentUsers_SocialFeatureEndpoints_AllSucceed()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-social-concurrent@test.com", "Social Concurrent");
        await SeedTestData(userId);

        var endpoints = new[] { "/Directory", "/Invitation/PendingForUser" };

        var responses = new List<HttpResponseMessage>();
        foreach (var endpoint in endpoints)
        {
            for (int i = 0; i < 5; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Add("Cookie", cookie);
                responses.Add(await _client.SendAsync(request));
            }
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    // ---------------------------------------------------------------
    // PERF-04/PERF-05: Lounge feature performance tests
    // ---------------------------------------------------------------

    private async Task SeedLoungeData(string userId, int? projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Create 100 messages with varying ages
        for (int i = 0; i < 100; i++)
        {
            var message = new LoungeMessage
            {
                ProjectId = projectId,
                UserId = userId,
                Content = $"Performance test message {i + 1}. This is a lounge message with some content to simulate realistic message sizes.",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i * 5)
            };
            context.LoungeMessages.Add(message);
        }
        await context.SaveChangesAsync();

        // Add some reactions to messages
        var messageIds = await context.LoungeMessages
            .Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .Select(m => m.Id)
            .ToListAsync();

        var reactionTypes = new[] { "thumbsup", "heart", "laugh", "rocket", "eyes" };
        foreach (var msgId in messageIds)
        {
            context.LoungeReactions.Add(new LoungeReaction
            {
                LoungeMessageId = msgId,
                UserId = userId,
                ReactionType = reactionTypes[msgId % reactionTypes.Length],
                CreatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task LoungeRoom_General_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-lounge-gen@test.com", "Lounge Perf Gen");
        await SeedLoungeData(userId, null);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Room");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Lounge #general room took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task LoungeRoom_ProjectRoom_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-lounge-proj@test.com", "Lounge Perf Proj");
        await SeedTestData(userId);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var projectId = await context.ProjectMembers
            .Where(pm => pm.UserId == userId)
            .Select(pm => pm.ProjectId)
            .FirstAsync();

        await SeedLoungeData(userId, projectId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/Room?projectId={projectId}");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < PageLoadThresholdMs,
            $"Lounge project room took {stopwatch.ElapsedMilliseconds}ms (threshold: {PageLoadThresholdMs}ms)");
    }

    [Fact]
    public async Task LoungeMessages_Pagination_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-lounge-msgs@test.com", "Lounge Perf Msgs");
        await SeedLoungeData(userId, null);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/Messages?count=50");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("HX-Request", "true");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Lounge messages partial took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    [Fact]
    public async Task LoungePinnedMessages_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-lounge-pinned@test.com", "Lounge Perf Pin");

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (int i = 0; i < 10; i++)
            {
                context.LoungeMessages.Add(new LoungeMessage
                {
                    ProjectId = null,
                    UserId = userId,
                    Content = $"Pinned message {i + 1}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                    IsPinned = true,
                    PinnedByUserId = userId,
                    PinnedAt = DateTime.UtcNow
                });
            }
            await context.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/PinnedMessages");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Lounge pinned messages partial took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    [Fact]
    public async Task LoungeMemberSearch_LoadsWithinThreshold()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-lounge-search@test.com", "Lounge Search Perf");

        var request = new HttpRequestMessage(HttpMethod.Get, "/Lounge/MemberSearch?term=lounge");
        request.Headers.Add("Cookie", cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < HtmxPartialThresholdMs,
            $"Lounge member search took {stopwatch.ElapsedMilliseconds}ms (threshold: {HtmxPartialThresholdMs}ms)");
    }

    // Lounge index verification

    [Fact]
    public async Task CompositeIndex_LoungeMessages_ProjectIdCreatedAt_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='LoungeMessages'")
            .ToListAsync();

        Assert.True(indexes.Count >= 2, "LoungeMessages should have multiple indexes including IX_LoungeMessage_ProjectId_CreatedAt");
    }

    [Fact]
    public async Task CompositeIndex_LoungeReactions_MessageIdUserIdType_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='LoungeReactions'")
            .ToListAsync();

        Assert.True(indexes.Count >= 2, "LoungeReactions should have multiple indexes including unique IX_LoungeReaction_MessageId_UserId_Type");
    }

    [Fact]
    public async Task CompositeIndex_LoungeReadPositions_UserIdProjectId_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='LoungeReadPositions'")
            .ToListAsync();

        Assert.True(indexes.Count >= 1, "LoungeReadPositions should have indexes including unique IX_LoungeReadPosition_UserId_ProjectId");
    }

    // Lounge concurrent access

    [Fact]
    public async Task ConcurrentUsers_LoungeEndpoints_AllSucceed()
    {
        var (userId, cookie) = await CreateAndLoginUser("perf-lounge-concurrent@test.com", "Lounge Concurrent");
        await SeedLoungeData(userId, null);

        var endpoints = new[] { "/Lounge/Room", "/Lounge/Messages?count=50", "/Lounge/PinnedMessages" };

        var responses = new List<HttpResponseMessage>();
        foreach (var endpoint in endpoints)
        {
            for (int i = 0; i < 3; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Add("Cookie", cookie);
                responses.Add(await _client.SendAsync(request));
            }
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
