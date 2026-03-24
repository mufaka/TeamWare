using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Views;

/// <summary>
/// Verifies AI UI/UX consistency across all rewrite (4) and summary (3) locations.
/// Covers: AI-UI-01, AI-UI-07, AI-UI-08, AI-UI-09, AI-NF-04, AI-NF-06, AI-NF-07, SEC-03.
/// </summary>
public class AiUiConsistencyTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiUiConsistencyTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(string Cookie, string UserId)> CreateAndLoginUser(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.EnsureCreatedAsync();

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = "AI Consistency Test User"
            };
            await userManager.CreateAsync(user, "TestPass1!");
        }

        var cookie = await GetLoginCookie(email);
        return (cookie, user.Id);
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

    private async Task SetOllamaUrl(string url)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var config = await context.GlobalConfigurations
            .FirstOrDefaultAsync(c => c.Key == "OLLAMA_URL");
        if (config != null)
        {
            config.Value = url;
            await context.SaveChangesAsync();
        }

        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        cache.Remove("OllamaConfig_OLLAMA_URL");
    }

    private async Task<int> CreateProject(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project
        {
            Name = $"AI Consistency Project {Guid.NewGuid():N}",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return project.Id;
    }

    private async Task<int> CreateTask(int projectId, string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var task = new TaskItem
        {
            Title = "AI Consistency Test Task",
            Description = "Some description",
            ProjectId = projectId,
            CreatedByUserId = userId,
            Status = TaskItemStatus.ToDo,
            Priority = TaskItemPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        return task.Id;
    }

    private async Task<int> CreateInboxItem(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var inboxItem = new InboxItem
        {
            Title = "AI Consistency Inbox Item",
            Description = "Some inbox description",
            UserId = userId,
            Status = InboxItemStatus.Unprocessed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.InboxItems.Add(inboxItem);
        await context.SaveChangesAsync();

        return inboxItem.Id;
    }

    private async Task<string> GetAuthenticatedPage(string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---------------------------------------------------------------
    // AI-UI-01: Rewrite buttons use consistent data-ai-rewrite pattern
    // ---------------------------------------------------------------

    [Fact]
    public async Task AllRewriteLocations_UseDataAiRewriteAttribute()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-rewrite1@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var taskId = await CreateTask(projectId, userId);
            var inboxItemId = await CreateInboxItem(userId);

            var projectEditHtml = await GetAuthenticatedPage($"/Project/Edit/{projectId}", cookie);
            var taskEditHtml = await GetAuthenticatedPage($"/Task/Edit/{taskId}", cookie);
            var taskDetailsHtml = await GetAuthenticatedPage($"/Task/Details/{taskId}", cookie);
            var inboxClarifyHtml = await GetAuthenticatedPage($"/Inbox/Clarify/{inboxItemId}", cookie);

            Assert.Contains("data-ai-rewrite", projectEditHtml);
            Assert.Contains("data-ai-rewrite", taskEditHtml);
            Assert.Contains("data-ai-rewrite", taskDetailsHtml);
            Assert.Contains("data-ai-rewrite", inboxClarifyHtml);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI-UI-01: Summary buttons use consistent data-ai-summary pattern
    // ---------------------------------------------------------------

    [Fact]
    public async Task Dashboard_UsesDataAiSummaryAttribute()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-summary-d@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);
            Assert.Contains("data-ai-summary", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task ProjectDetails_UsesDataAiSummaryAttribute()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-summary-p@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
            Assert.Contains("data-ai-summary", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_UsesDataAiSummaryAttribute()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-summary-r@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);
            Assert.Contains("data-ai-summary", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI-UI-01: All AI buttons reference the shared JS modules
    // ---------------------------------------------------------------

    [Fact]
    public async Task AllRewriteLocations_IncludeAiRewriteJsModule()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-js1@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var taskId = await CreateTask(projectId, userId);
            var inboxItemId = await CreateInboxItem(userId);

            var projectEditHtml = await GetAuthenticatedPage($"/Project/Edit/{projectId}", cookie);
            var taskEditHtml = await GetAuthenticatedPage($"/Task/Edit/{taskId}", cookie);
            var taskDetailsHtml = await GetAuthenticatedPage($"/Task/Details/{taskId}", cookie);
            var inboxClarifyHtml = await GetAuthenticatedPage($"/Inbox/Clarify/{inboxItemId}", cookie);

            Assert.Contains("ai-rewrite.js", projectEditHtml);
            Assert.Contains("ai-rewrite.js", taskEditHtml);
            Assert.Contains("ai-rewrite.js", taskDetailsHtml);
            Assert.Contains("ai-rewrite.js", inboxClarifyHtml);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Dashboard_IncludesAiSummaryJsModule()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-js-d@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);
            Assert.Contains("ai-summary.js", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task ProjectDetails_IncludesAiSummaryJsModule()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-js-p@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
            Assert.Contains("ai-summary.js", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_IncludesAiSummaryJsModule()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-js-r@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);
            Assert.Contains("ai-summary.js", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI-UI-01: All rewrite buttons specify data-ai-label
    // ---------------------------------------------------------------

    [Fact]
    public async Task AllRewriteLocations_SpecifyDataAiLabel()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-label1@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var taskId = await CreateTask(projectId, userId);
            var inboxItemId = await CreateInboxItem(userId);

            var projectEditHtml = await GetAuthenticatedPage($"/Project/Edit/{projectId}", cookie);
            var taskEditHtml = await GetAuthenticatedPage($"/Task/Edit/{taskId}", cookie);
            var taskDetailsHtml = await GetAuthenticatedPage($"/Task/Details/{taskId}", cookie);
            var inboxClarifyHtml = await GetAuthenticatedPage($"/Inbox/Clarify/{inboxItemId}", cookie);

            Assert.Contains("data-ai-label=\"AI Rewrite\"", projectEditHtml);
            Assert.Contains("data-ai-label=\"AI Rewrite\"", taskEditHtml);
            Assert.Contains("data-ai-label=\"Polish\"", taskDetailsHtml);
            Assert.Contains("data-ai-label=\"Expand\"", inboxClarifyHtml);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Dashboard_SpecifiesGenerateSummaryLabel()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-label-d@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);
            Assert.Contains("data-ai-label=\"Generate Summary\"", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task ProjectDetails_SpecifiesGenerateSummaryLabel()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-label-p@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
            Assert.Contains("data-ai-label=\"Generate Summary\"", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_SpecifiesPrepareReviewSummaryLabel()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-label-r@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);
            Assert.Contains("data-ai-label=\"Prepare Review Summary\"", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI-NF-07: No emoji or emoticon characters in AI labels
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("AI Rewrite")]
    [InlineData("Polish")]
    [InlineData("Expand")]
    [InlineData("Generate Summary")]
    [InlineData("Prepare Review Summary")]
    public void AiLabels_ContainNoEmoji(string label)
    {
        foreach (var c in label)
        {
            var codePoint = (int)c;

            Assert.False(codePoint >= 0x2600 && codePoint <= 0x26FF,
                $"Emoji character found in AI label '{label}': U+{codePoint:X4}");
            Assert.False(codePoint >= 0x2700 && codePoint <= 0x27BF,
                $"Dingbat character found in AI label '{label}': U+{codePoint:X4}");
        }

        var emojiPattern = @"[\uD83C-\uD83F][\uDC00-\uDFFF]";
        Assert.False(Regex.IsMatch(label, emojiPattern),
            $"Supplementary emoji character found in AI label '{label}'");
    }

    // ---------------------------------------------------------------
    // AI-NF-07: No emoji in AI JS module string literals
    // ---------------------------------------------------------------

    [Fact]
    public void AiRewriteJs_ContainsNoEmoji()
    {
        var jsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TeamWare.Web", "wwwroot", "js", "ai-rewrite.js");
        if (!File.Exists(jsPath))
        {
            jsPath = Path.GetFullPath(Path.Combine(".", "TeamWare.Web", "wwwroot", "js", "ai-rewrite.js"));
        }

        if (File.Exists(jsPath))
        {
            var content = File.ReadAllText(jsPath);
            AssertNoEmojis(content, "ai-rewrite.js");
        }
    }

    [Fact]
    public void AiSummaryJs_ContainsNoEmoji()
    {
        var jsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TeamWare.Web", "wwwroot", "js", "ai-summary.js");
        if (!File.Exists(jsPath))
        {
            jsPath = Path.GetFullPath(Path.Combine(".", "TeamWare.Web", "wwwroot", "js", "ai-summary.js"));
        }

        if (File.Exists(jsPath))
        {
            var content = File.ReadAllText(jsPath);
            AssertNoEmojis(content, "ai-summary.js");
        }
    }

    // ---------------------------------------------------------------
    // AI-UI-07, AI-NF-06: Dark mode classes on rewrite pages
    // ---------------------------------------------------------------

    [Fact]
    public async Task RewritePages_ContainDarkModeClasses()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-dark-rw@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var taskId = await CreateTask(projectId, userId);
            var inboxItemId = await CreateInboxItem(userId);

            var pages = new[]
            {
                ($"/Project/Edit/{projectId}", "Project Edit"),
                ($"/Task/Edit/{taskId}", "Task Edit"),
                ($"/Task/Details/{taskId}", "Task Details"),
                ($"/Inbox/Clarify/{inboxItemId}", "Inbox Clarify")
            };

            foreach (var (url, pageName) in pages)
            {
                var html = await GetAuthenticatedPage(url, cookie);
                Assert.True(html.Contains("dark:", StringComparison.Ordinal),
                    $"Dark mode classes missing on {pageName} page");
            }
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Dashboard_ContainsDarkModeClasses()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-dark-d@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);
            Assert.True(html.Contains("dark:", StringComparison.Ordinal),
                "Dark mode classes missing on Dashboard page");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task ProjectDetails_ContainsDarkModeClasses()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-dark-p@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
            Assert.True(html.Contains("dark:", StringComparison.Ordinal),
                "Dark mode classes missing on Project Details page");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_ContainsDarkModeClasses()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-dark-r@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);
            Assert.True(html.Contains("dark:", StringComparison.Ordinal),
                "Dark mode classes missing on Review page");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI-UI-07: AI rewrite containers use consistent wrapper class
    // ---------------------------------------------------------------

    [Fact]
    public async Task AllRewriteContainers_HaveConsistentWrapperClass()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-wrap1@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var taskId = await CreateTask(projectId, userId);
            var inboxItemId = await CreateInboxItem(userId);

            var pages = new[]
            {
                ($"/Project/Edit/{projectId}", "Project Edit"),
                ($"/Task/Edit/{taskId}", "Task Edit"),
                ($"/Task/Details/{taskId}", "Task Details (Comment)"),
                ($"/Inbox/Clarify/{inboxItemId}", "Inbox Clarify")
            };

            foreach (var (url, pageName) in pages)
            {
                var html = await GetAuthenticatedPage(url, cookie);
                var pattern = @"data-ai-rewrite[^>]*class=""mt-2""";
                Assert.True(Regex.IsMatch(html, pattern),
                    $"Rewrite container on {pageName} does not have consistent mt-2 wrapper class");
            }
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI-UI-08: AI-enabled pages contain responsive layout classes
    // ---------------------------------------------------------------

    [Fact]
    public async Task Dashboard_ContainsResponsiveLayoutClasses()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-resp-d@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);
            Assert.True(html.Contains("max-w-", StringComparison.Ordinal),
                "Responsive max-width class missing on Dashboard");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task ProjectDetails_ContainsResponsiveLayoutClasses()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-resp-p@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
            // Project Details uses responsive breakpoint classes (sm:, md:, lg:) instead of max-w-
            Assert.True(html.Contains("sm:", StringComparison.Ordinal),
                "Responsive breakpoint classes missing on Project Details");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_ContainsResponsiveLayoutClasses()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-resp-r@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);
            Assert.True(html.Contains("max-w-", StringComparison.Ordinal),
                "Responsive max-width class missing on Review");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI-NF-04: AI buttons not rendered when OLLAMA_URL is empty
    // ---------------------------------------------------------------

    [Fact]
    public async Task RewriteLocations_HiddenWhenOllamaNotConfigured()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-hidden-rw@test.com");

        var projectId = await CreateProject(userId);
        var taskId = await CreateTask(projectId, userId);
        var inboxItemId = await CreateInboxItem(userId);

        var pages = new[]
        {
            ($"/Project/Edit/{projectId}", "Project Edit"),
            ($"/Task/Edit/{taskId}", "Task Edit"),
            ($"/Task/Details/{taskId}", "Task Details"),
            ($"/Inbox/Clarify/{inboxItemId}", "Inbox Clarify")
        };

        foreach (var (url, pageName) in pages)
        {
            var html = await GetAuthenticatedPage(url, cookie);
            Assert.DoesNotContain("data-ai-rewrite", html);
        }
    }

    [Fact]
    public async Task Dashboard_HiddenWhenOllamaNotConfigured()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-hidden-d@test.com");

        var html = await GetAuthenticatedPage("/", cookie);
        Assert.DoesNotContain("data-ai-summary", html);
    }

    [Fact]
    public async Task ProjectDetails_HiddenWhenOllamaNotConfigured()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-hidden-p@test.com");

        var projectId = await CreateProject(userId);
        var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
        Assert.DoesNotContain("data-ai-summary", html);
    }

    [Fact]
    public async Task Review_HiddenWhenOllamaNotConfigured()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-hidden-r@test.com");

        var html = await GetAuthenticatedPage("/Review", cookie);
        Assert.DoesNotContain("data-ai-summary", html);
    }

    // ---------------------------------------------------------------
    // AI-UI-09: Project summary has period selector
    // ---------------------------------------------------------------

    [Fact]
    public async Task ProjectSummary_ContainsPeriodSelector()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-period@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
            Assert.Contains("data-ai-periods=\"Today,ThisWeek,ThisMonth\"", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI-UI-09: Personal dashboard and review do NOT have period selectors
    // ---------------------------------------------------------------

    [Fact]
    public async Task Dashboard_DoesNotContainPeriodSelector()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-noperiod-d@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);
            Assert.DoesNotContain("data-ai-periods", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_DoesNotContainPeriodSelector()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-noperiod-r@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);
            Assert.DoesNotContain("data-ai-periods", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // AI rewrite containers include data-ai-url pointing to correct endpoint
    // ---------------------------------------------------------------

    [Fact]
    public async Task RewriteLocations_HaveCorrectEndpointUrls()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-urls1@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var taskId = await CreateTask(projectId, userId);
            var inboxItemId = await CreateInboxItem(userId);

            var projectEditHtml = await GetAuthenticatedPage($"/Project/Edit/{projectId}", cookie);
            var taskEditHtml = await GetAuthenticatedPage($"/Task/Edit/{taskId}", cookie);
            var taskDetailsHtml = await GetAuthenticatedPage($"/Task/Details/{taskId}", cookie);
            var inboxClarifyHtml = await GetAuthenticatedPage($"/Inbox/Clarify/{inboxItemId}", cookie);

            Assert.Contains("/Ai/RewriteProjectDescription", projectEditHtml);
            Assert.Contains("/Ai/RewriteTaskDescription", taskEditHtml);
            Assert.Contains("/Ai/PolishComment", taskDetailsHtml);
            Assert.Contains("/Ai/ExpandInboxItem", inboxClarifyHtml);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Dashboard_HasCorrectEndpointUrl()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-urls-d@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);
            Assert.Contains("/Ai/PersonalDigest", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task ProjectDetails_HasCorrectEndpointUrl()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-urls-p@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
            Assert.Contains("/Ai/ProjectSummary", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_HasCorrectEndpointUrl()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-urls-r@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);
            Assert.Contains("/Ai/ReviewPreparation", html);
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // SEC-03: Summary forms include anti-forgery tokens
    // ---------------------------------------------------------------

    [Fact]
    public async Task Dashboard_SummaryFormContainsAntiForgeryToken()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-csrf-d@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/", cookie);
            AssertSummaryFormHasAntiForgeryToken(html, "Dashboard");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task ProjectDetails_SummaryFormContainsAntiForgeryToken()
    {
        var (cookie, userId) = await CreateAndLoginUser("ai-consist-csrf-p@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var projectId = await CreateProject(userId);
            var html = await GetAuthenticatedPage($"/Project/Details/{projectId}", cookie);
            AssertSummaryFormHasAntiForgeryToken(html, "Project Details");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    [Fact]
    public async Task Review_SummaryFormContainsAntiForgeryToken()
    {
        var (cookie, _) = await CreateAndLoginUser("ai-consist-csrf-r@test.com");
        await SetOllamaUrl("http://localhost:11434");

        try
        {
            var html = await GetAuthenticatedPage("/Review", cookie);
            AssertSummaryFormHasAntiForgeryToken(html, "Review");
        }
        finally
        {
            await SetOllamaUrl("");
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static void AssertSummaryFormHasAntiForgeryToken(string html, string pageName)
    {
        var summaryIndex = html.IndexOf("data-ai-summary", StringComparison.Ordinal);
        Assert.True(summaryIndex > 0, $"AI summary not found on {pageName}");

        var formStart = html.LastIndexOf("<form", summaryIndex, StringComparison.Ordinal);
        Assert.True(formStart > 0, $"No form wrapper for AI summary on {pageName}");

        var formEnd = html.IndexOf("</form>", summaryIndex, StringComparison.Ordinal);
        Assert.True(formEnd > 0, $"No closing form tag for AI summary on {pageName}");

        var formHtml = html[formStart..formEnd];
        Assert.Contains("__RequestVerificationToken", formHtml);
    }

    private static void AssertNoEmojis(string content, string source)
    {
        foreach (var c in content)
        {
            var codePoint = (int)c;

            if (codePoint >= 0x2600 && codePoint <= 0x26FF)
            {
                Assert.Fail($"Emoji character found in {source}: U+{codePoint:X4}");
            }
            if (codePoint >= 0x2700 && codePoint <= 0x27BF)
            {
                Assert.Fail($"Dingbat/emoji character found in {source}: U+{codePoint:X4}");
            }
        }

        var emojiPattern = @"[\uD83C-\uD83F][\uDC00-\uDFFF]";
        Assert.False(Regex.IsMatch(content, emojiPattern),
            $"Supplementary emoji character found in {source}");
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
