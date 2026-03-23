using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class LoungeAttachmentControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _tempDir;

    public LoungeAttachmentControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        _tempDir = Path.Combine(Path.GetTempPath(), $"teamware_lounge_attach_test_{Guid.NewGuid()}");
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

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email = "lounge-attach@test.com", string displayName = "Lounge Attach User")
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

    private async Task<(int ProjectId, int MessageId)> CreateProjectAndMessage(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project { Name = $"LoungeAttachProject-{Guid.NewGuid():N}" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner
        });
        await context.SaveChangesAsync();

        var message = new LoungeMessage
        {
            ProjectId = project.Id,
            UserId = userId,
            Content = "Test lounge message for attachments"
        };
        context.LoungeMessages.Add(message);
        await context.SaveChangesAsync();

        return (project.Id, message.Id);
    }

    private async Task<int> CreateGeneralMessage(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = new LoungeMessage
        {
            ProjectId = null,
            UserId = userId,
            Content = "Test general lounge message for attachments"
        };
        context.LoungeMessages.Add(message);
        await context.SaveChangesAsync();

        return message.Id;
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

        var response = await _client.PostAsync("/Lounge/UploadAttachment?projectId=1", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task UploadAttachment_AsMember_Succeeds()
    {
        var (userId, cookie) = await CreateAndLoginUser("lounge-upload-member@test.com", "Lounge Upload Member");
        var (projectId, _) = await CreateProjectAndMessage(userId);

        var token = await GetAntiForgeryToken(cookie, $"/Lounge/Room?projectId={projectId}");

        var fileContent = new ByteArrayContent("lounge file content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var formContent = new MultipartFormDataContent();
        formContent.Add(fileContent, "file", "lounge-file.txt");
        formContent.Add(new StringContent("Here is my file"), "content");
        formContent.Add(new StringContent(token), "__RequestVerificationToken");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Lounge/UploadAttachment?projectId={projectId}");
        request.Headers.Add("Cookie", cookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // A new message should have been created with the attachment
        var newMessage = await context.LoungeMessages
            .Where(m => m.ProjectId == projectId && m.Content == "Here is my file")
            .FirstOrDefaultAsync();
        Assert.NotNull(newMessage);
        var attachment = await context.Attachments.FirstOrDefaultAsync(a => a.EntityId == newMessage.Id && a.EntityType == AttachmentEntityType.LoungeMessage);
        Assert.NotNull(attachment);
        Assert.Equal("lounge-file.txt", attachment.FileName);
    }

    [Fact]
    public async Task UploadAttachment_GeneralRoom_Succeeds()
    {
        var (userId, cookie) = await CreateAndLoginUser("lounge-upload-general@test.com", "Lounge Upload General");

        var token = await GetAntiForgeryToken(cookie, "/Lounge/Room");

        var fileContent = new ByteArrayContent("general room file"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var formContent = new MultipartFormDataContent();
        formContent.Add(fileContent, "file", "general-file.txt");
        formContent.Add(new StringContent(token), "__RequestVerificationToken");
        // No content field — should default to "Attached a file"

        var request = new HttpRequestMessage(HttpMethod.Post, "/Lounge/UploadAttachment");
        request.Headers.Add("Cookie", cookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // A new message with default content should have been created
        var newMessage = await context.LoungeMessages
            .Where(m => m.ProjectId == null && m.Content == "Attached a file" && m.UserId == userId)
            .FirstOrDefaultAsync();
        Assert.NotNull(newMessage);
        var attachment = await context.Attachments.FirstOrDefaultAsync(a => a.EntityId == newMessage.Id && a.EntityType == AttachmentEntityType.LoungeMessage);
        Assert.NotNull(attachment);
        Assert.Equal("general-file.txt", attachment.FileName);
    }

    // --- Download ---

    [Fact]
    public async Task DownloadAttachment_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Lounge/DownloadAttachment?messageId=1&attachmentId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DownloadAttachment_AsMember_ReturnsFile()
    {
        var (userId, cookie) = await CreateAndLoginUser("lounge-download@test.com", "Lounge Download");
        var (projectId, messageId) = await CreateProjectAndMessage(userId);

        var storedName = $"{Guid.NewGuid()}.txt";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, storedName), "lounge download content");

        int attachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var att = new Attachment
            {
                FileName = "download-lounge.txt",
                StoredFileName = storedName,
                ContentType = "text/plain",
                FileSizeBytes = 22,
                EntityType = AttachmentEntityType.LoungeMessage,
                EntityId = messageId,
                UploadedByUserId = userId
            };
            context.Attachments.Add(att);
            await context.SaveChangesAsync();
            attachmentId = att.Id;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Lounge/DownloadAttachment?messageId={messageId}&attachmentId={attachmentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Equal("lounge download content", responseContent);
    }

    // --- Delete ---

    [Fact]
    public async Task DeleteAttachment_Unauthenticated_RedirectsToLogin()
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>());
        var response = await _client.PostAsync("/Lounge/DeleteAttachment?messageId=1&attachmentId=1", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DeleteAttachment_AsUploader_Succeeds()
    {
        var (userId, cookie) = await CreateAndLoginUser("lounge-delete-uploader@test.com", "Lounge Delete Uploader");
        var (projectId, messageId) = await CreateProjectAndMessage(userId);

        var storedName = $"{Guid.NewGuid()}.txt";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, storedName), "to delete");

        int attachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var att = new Attachment
            {
                FileName = "delete-lounge-attach.txt",
                StoredFileName = storedName,
                ContentType = "text/plain",
                FileSizeBytes = 9,
                EntityType = AttachmentEntityType.LoungeMessage,
                EntityId = messageId,
                UploadedByUserId = userId
            };
            context.Attachments.Add(att);
            await context.SaveChangesAsync();
            attachmentId = att.Id;
        }

        var token = await GetAntiForgeryToken(cookie, $"/Lounge/Room?projectId={projectId}");

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Lounge/DeleteAttachment?messageId={messageId}&attachmentId={attachmentId}");
        request.Headers.Add("Cookie", cookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var context2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deleted = await context2.Attachments.FindAsync(attachmentId);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAttachment_NonUploader_NonOwner_Fails()
    {
        var (ownerId, ownerCookie) = await CreateAndLoginUser("lounge-delete-owner@test.com", "Lounge Delete Owner");
        var (projectId, messageId) = await CreateProjectAndMessage(ownerId);

        var storedName = $"{Guid.NewGuid()}.txt";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, storedName), "to keep");

        int attachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var att = new Attachment
            {
                FileName = "keep-lounge-attach.txt",
                StoredFileName = storedName,
                ContentType = "text/plain",
                FileSizeBytes = 7,
                EntityType = AttachmentEntityType.LoungeMessage,
                EntityId = messageId,
                UploadedByUserId = ownerId
            };
            context.Attachments.Add(att);
            await context.SaveChangesAsync();
            attachmentId = att.Id;
        }

        // Create a different user who is a member but not the uploader/owner
        var (otherUserId, otherCookie) = await CreateAndLoginUser("lounge-delete-other@test.com", "Lounge Delete Other");
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                UserId = otherUserId,
                Role = ProjectRole.Member
            });
            await context.SaveChangesAsync();
        }

        var token = await GetAntiForgeryToken(otherCookie, $"/Lounge/Room?projectId={projectId}");

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Lounge/DeleteAttachment?messageId={messageId}&attachmentId={attachmentId}");
        request.Headers.Add("Cookie", otherCookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        // Should redirect back (with error TempData) but attachment remains
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var context2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var still = await context2.Attachments.FindAsync(attachmentId);
        Assert.NotNull(still);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
