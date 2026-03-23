using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class ProjectAttachmentControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _tempDir;

    public ProjectAttachmentControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        _tempDir = Path.Combine(Path.GetTempPath(), $"teamware_attachment_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        SeedGlobalConfiguration();
    }

    private void SeedGlobalConfiguration()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated();

        if (!context.GlobalConfigurations.Any(gc => gc.Key == "ATTACHMENT_DIR"))
        {
            context.GlobalConfigurations.Add(new GlobalConfiguration
            {
                Key = "ATTACHMENT_DIR",
                Value = _tempDir,
                Description = "Test attachment directory"
            });
            context.SaveChanges();
        }
        else
        {
            var config = context.GlobalConfigurations.First(gc => gc.Key == "ATTACHMENT_DIR");
            config.Value = _tempDir;
            context.SaveChanges();
        }
    }

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email = "attach-test@test.com", string displayName = "Attach User")
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

        var loginResponse = await _client.SendAsync(request);
        var loginCookies = loginResponse.Headers.GetValues("Set-Cookie");

        return string.Join("; ", loginCookies);
    }

    private async Task<int> CreateProjectWithMember(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project { Name = $"AttachProject-{Guid.NewGuid():N}" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner
        });
        await context.SaveChangesAsync();

        return project.Id;
    }

    private async Task<string> GetAntiForgeryToken(string cookie, string url)
    {
        var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
        getRequest.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(getRequest);
        var content = await response.Content.ReadAsStringAsync();

        return ExtractAntiForgeryToken(content);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var tokenStart = html.IndexOf("name=\"__RequestVerificationToken\"", StringComparison.Ordinal);
        if (tokenStart == -1) return string.Empty;

        var valueStart = html.IndexOf("value=\"", tokenStart, StringComparison.Ordinal) + 7;
        var valueEnd = html.IndexOf("\"", valueStart, StringComparison.Ordinal);
        return html[valueStart..valueEnd];
    }

    // --- Upload ---

    [Fact]
    public async Task UploadAttachment_Unauthenticated_RedirectsToLogin()
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([1, 2, 3]), "file", "test.txt");

        var response = await _client.PostAsync("/Project/UploadAttachment?projectId=1", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task UploadAttachment_AsMember_Succeeds()
    {
        var (userId, cookie) = await CreateAndLoginUser("upload-member@test.com", "Upload Member");
        var projectId = await CreateProjectWithMember(userId);

        var token = await GetAntiForgeryToken(cookie, $"/Project/Details/{projectId}");

        var fileContent = new ByteArrayContent("test file content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var formContent = new MultipartFormDataContent();
        formContent.Add(fileContent, "file", "test-upload.txt");
        formContent.Add(new StringContent(token), "__RequestVerificationToken");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Project/UploadAttachment?projectId={projectId}");
        request.Headers.Add("Cookie", cookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Project/Details/{projectId}", response.Headers.Location?.ToString());

        // Verify file was saved
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var attachment = await context.Attachments.FirstOrDefaultAsync(a => a.EntityId == projectId && a.EntityType == AttachmentEntityType.Project);
        Assert.NotNull(attachment);
        Assert.Equal("test-upload.txt", attachment.FileName);
    }

    // --- Download ---

    [Fact]
    public async Task DownloadAttachment_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Project/DownloadAttachment?projectId=1&attachmentId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DownloadAttachment_AsMember_ReturnsFile()
    {
        var (userId, cookie) = await CreateAndLoginUser("download-member@test.com", "Download Member");
        var projectId = await CreateProjectWithMember(userId);

        // Seed an attachment directly
        var storedName = $"{Guid.NewGuid()}.txt";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, storedName), "download content");

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Attachments.Add(new Attachment
            {
                FileName = "download-test.txt",
                StoredFileName = storedName,
                ContentType = "text/plain",
                FileSizeBytes = 16,
                EntityType = AttachmentEntityType.Project,
                EntityId = projectId,
                UploadedByUserId = userId
            });
            await context.SaveChangesAsync();
        }

        int attachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            attachmentId = (await context.Attachments.FirstAsync(a => a.StoredFileName == storedName)).Id;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Project/DownloadAttachment?projectId={projectId}&attachmentId={attachmentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Equal("download content", responseContent);
    }

    // --- Delete ---

    [Fact]
    public async Task DeleteAttachment_Unauthenticated_RedirectsToLogin()
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>());
        var response = await _client.PostAsync("/Project/DeleteAttachment?projectId=1&attachmentId=1", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DeleteAttachment_AsUploader_Succeeds()
    {
        var (userId, cookie) = await CreateAndLoginUser("delete-uploader@test.com", "Delete Uploader");
        var projectId = await CreateProjectWithMember(userId);

        var storedName = $"{Guid.NewGuid()}.txt";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, storedName), "to delete");

        int attachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var att = new Attachment
            {
                FileName = "delete-test.txt",
                StoredFileName = storedName,
                ContentType = "text/plain",
                FileSizeBytes = 9,
                EntityType = AttachmentEntityType.Project,
                EntityId = projectId,
                UploadedByUserId = userId
            };
            context.Attachments.Add(att);
            await context.SaveChangesAsync();
            attachmentId = att.Id;
        }

        var token = await GetAntiForgeryToken(cookie, $"/Project/Details/{projectId}");

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Project/DeleteAttachment?projectId={projectId}&attachmentId={attachmentId}");
        request.Headers.Add("Cookie", cookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deleted = await verifyContext.Attachments.FindAsync(attachmentId);
        Assert.Null(deleted);
    }

    // --- Non-member authorization ---

    [Fact]
    public async Task UploadAttachment_NonMember_Redirects()
    {
        var (ownerId, _) = await CreateAndLoginUser("owner-nonmember@test.com", "Owner");
        var projectId = await CreateProjectWithMember(ownerId);

        var (_, nonMemberCookie) = await CreateAndLoginUser("nonmember-upload@test.com", "Non Member");

        var token = await GetAntiForgeryToken(nonMemberCookie, "/Project");

        var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var formContent = new MultipartFormDataContent();
        formContent.Add(fileContent, "file", "bad.txt");
        formContent.Add(new StringContent(token), "__RequestVerificationToken");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Project/UploadAttachment?projectId={projectId}");
        request.Headers.Add("Cookie", nonMemberCookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // --- Details view includes attachments section ---

    [Fact]
    public async Task Details_ShowsAttachmentsSection()
    {
        var (userId, cookie) = await CreateAndLoginUser("details-attach@test.com", "Details Attach");
        var projectId = await CreateProjectWithMember(userId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Project/Details/{projectId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Attachments", content);
    }

    public void Dispose()
    {
        _client.Dispose();

        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
