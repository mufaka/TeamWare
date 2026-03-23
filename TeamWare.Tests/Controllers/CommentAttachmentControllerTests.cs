using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class CommentAttachmentControllerTests : IClassFixture<TeamWareWebApplicationFactory>, IDisposable
{
    private readonly TeamWareWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _tempDir;

    public CommentAttachmentControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        _tempDir = Path.Combine(Path.GetTempPath(), $"teamware_comment_attach_test_{Guid.NewGuid()}");
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

    private async Task<(string UserId, string Cookie)> CreateAndLoginUser(string email = "comment-attach@test.com", string displayName = "Comment Attach User")
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

    private async Task<(int ProjectId, int TaskId, int CommentId)> CreateProjectTaskAndComment(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project { Name = $"CommentAttachProject-{Guid.NewGuid():N}" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner
        });
        await context.SaveChangesAsync();

        var task = new TaskItem
        {
            Title = "Comment Attach Task",
            ProjectId = project.Id,
            CreatedByUserId = userId,
            Status = TaskItemStatus.ToDo,
            Priority = TaskItemPriority.Medium
        };
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var comment = new Comment
        {
            TaskItemId = task.Id,
            AuthorId = userId,
            Content = "Test comment for attachments"
        };
        context.Comments.Add(comment);
        await context.SaveChangesAsync();

        return (project.Id, task.Id, comment.Id);
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

        var response = await _client.PostAsync("/Comment/UploadAttachment?commentId=1", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task UploadAttachment_AsMember_Succeeds()
    {
        var (userId, cookie) = await CreateAndLoginUser("comment-upload-member@test.com", "Comment Upload Member");
        var (projectId, taskId, commentId) = await CreateProjectTaskAndComment(userId);

        var token = await GetAntiForgeryToken(cookie, $"/Task/Details/{taskId}");

        var fileContent = new ByteArrayContent("comment file content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var formContent = new MultipartFormDataContent();
        formContent.Add(fileContent, "file", "comment-file.txt");
        formContent.Add(new StringContent(token), "__RequestVerificationToken");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Comment/UploadAttachment?commentId={commentId}");
        request.Headers.Add("Cookie", cookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Task/Details/{taskId}", response.Headers.Location?.ToString());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var attachment = await context.Attachments.FirstOrDefaultAsync(a => a.EntityId == commentId && a.EntityType == AttachmentEntityType.Comment);
        Assert.NotNull(attachment);
        Assert.Equal("comment-file.txt", attachment.FileName);
    }

    // --- Download ---

    [Fact]
    public async Task DownloadAttachment_Unauthenticated_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Comment/DownloadAttachment?commentId=1&attachmentId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DownloadAttachment_AsMember_ReturnsFile()
    {
        var (userId, cookie) = await CreateAndLoginUser("comment-download@test.com", "Comment Download");
        var (projectId, taskId, commentId) = await CreateProjectTaskAndComment(userId);

        var storedName = $"{Guid.NewGuid()}.txt";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, storedName), "comment download content");

        int attachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var att = new Attachment
            {
                FileName = "download-comment.txt",
                StoredFileName = storedName,
                ContentType = "text/plain",
                FileSizeBytes = 24,
                EntityType = AttachmentEntityType.Comment,
                EntityId = commentId,
                UploadedByUserId = userId
            };
            context.Attachments.Add(att);
            await context.SaveChangesAsync();
            attachmentId = att.Id;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Comment/DownloadAttachment?commentId={commentId}&attachmentId={attachmentId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Equal("comment download content", responseContent);
    }

    // --- Delete ---

    [Fact]
    public async Task DeleteAttachment_Unauthenticated_RedirectsToLogin()
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>());
        var response = await _client.PostAsync("/Comment/DeleteAttachment?commentId=1&attachmentId=1", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DeleteAttachment_AsUploader_Succeeds()
    {
        var (userId, cookie) = await CreateAndLoginUser("comment-delete-uploader@test.com", "Comment Delete Uploader");
        var (projectId, taskId, commentId) = await CreateProjectTaskAndComment(userId);

        var storedName = $"{Guid.NewGuid()}.txt";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, storedName), "to delete");

        int attachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var att = new Attachment
            {
                FileName = "delete-comment-attach.txt",
                StoredFileName = storedName,
                ContentType = "text/plain",
                FileSizeBytes = 9,
                EntityType = AttachmentEntityType.Comment,
                EntityId = commentId,
                UploadedByUserId = userId
            };
            context.Attachments.Add(att);
            await context.SaveChangesAsync();
            attachmentId = att.Id;
        }

        var token = await GetAntiForgeryToken(cookie, $"/Task/Details/{taskId}");

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Comment/DeleteAttachment?commentId={commentId}&attachmentId={attachmentId}");
        request.Headers.Add("Cookie", cookie);
        request.Content = formContent;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deleted = await verifyContext.Attachments.FindAsync(attachmentId);
        Assert.Null(deleted);
    }

    // --- Task Details view includes comment attachments ---

    [Fact]
    public async Task TaskDetails_ShowsCommentAttachments()
    {
        var (userId, cookie) = await CreateAndLoginUser("comment-attach-view@test.com", "Comment Attach View");
        var (projectId, taskId, commentId) = await CreateProjectTaskAndComment(userId);

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Attachments.Add(new Attachment
            {
                FileName = "visible-comment-file.txt",
                StoredFileName = $"{Guid.NewGuid()}.txt",
                ContentType = "text/plain",
                FileSizeBytes = 100,
                EntityType = AttachmentEntityType.Comment,
                EntityId = commentId,
                UploadedByUserId = userId
            });
            await context.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Task/Details/{taskId}");
        request.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("visible-comment-file.txt", content);
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
