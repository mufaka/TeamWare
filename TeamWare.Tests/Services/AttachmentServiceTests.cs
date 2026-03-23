using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class AttachmentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly AttachmentService _service;
    private readonly InMemoryFileStorageService _fileStorage;
    private readonly ApplicationUser _user;
    private readonly Project _project;

    public AttachmentServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _user = new ApplicationUser
        {
            UserName = "user@test.com",
            Email = "user@test.com",
            DisplayName = "Test User"
        };
        _context.Users.Add(_user);

        _project = new Project { Name = "Test Project" };
        _context.Projects.Add(_project);
        _context.SaveChanges();

        _fileStorage = new InMemoryFileStorageService();
        _service = new AttachmentService(_context, _fileStorage);
    }

    // --- UploadAsync ---

    [Fact]
    public async Task UploadAsync_CreatesAttachmentAndSavesFile()
    {
        using var stream = new MemoryStream("file content"u8.ToArray());

        var result = await _service.UploadAsync(
            stream, "report.pdf", "application/pdf", 12,
            AttachmentEntityType.Project, _project.Id, _user.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("report.pdf", result.Data.FileName);
        Assert.Equal("application/pdf", result.Data.ContentType);
        Assert.Equal(12, result.Data.FileSizeBytes);
        Assert.Equal(AttachmentEntityType.Project, result.Data.EntityType);
        Assert.Equal(_project.Id, result.Data.EntityId);
        Assert.Equal(_user.Id, result.Data.UploadedByUserId);
        Assert.EndsWith(".pdf", result.Data.StoredFileName);
        Assert.Single(_fileStorage.SavedFiles);
    }

    [Fact]
    public async Task UploadAsync_GeneratesUniqueStoredFileName()
    {
        using var stream1 = new MemoryStream("a"u8.ToArray());
        using var stream2 = new MemoryStream("b"u8.ToArray());

        var result1 = await _service.UploadAsync(
            stream1, "file.txt", "text/plain", 1,
            AttachmentEntityType.Project, _project.Id, _user.Id);

        var result2 = await _service.UploadAsync(
            stream2, "file.txt", "text/plain", 1,
            AttachmentEntityType.Project, _project.Id, _user.Id);

        Assert.True(result1.Succeeded);
        Assert.True(result2.Succeeded);
        Assert.NotEqual(result1.Data!.StoredFileName, result2.Data!.StoredFileName);
    }

    [Fact]
    public async Task UploadAsync_ReturnsFailureWhenFileSaveFails()
    {
        _fileStorage.ShouldFail = true;
        using var stream = new MemoryStream("data"u8.ToArray());

        var result = await _service.UploadAsync(
            stream, "bad.txt", "text/plain", 4,
            AttachmentEntityType.Project, _project.Id, _user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Failed to save file", result.Errors[0]);
        Assert.Empty(await _context.Attachments.ToListAsync());
    }

    // --- GetAttachmentsAsync ---

    [Fact]
    public async Task GetAttachmentsAsync_ReturnsAttachmentsForEntity()
    {
        await SeedAttachmentAsync("file1.txt", AttachmentEntityType.Project, _project.Id);
        await SeedAttachmentAsync("file2.txt", AttachmentEntityType.Project, _project.Id);
        await SeedAttachmentAsync("other.txt", AttachmentEntityType.Comment, 99);

        var result = await _service.GetAttachmentsAsync(AttachmentEntityType.Project, _project.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, a => Assert.Equal(AttachmentEntityType.Project, a.EntityType));
    }

    [Fact]
    public async Task GetAttachmentsAsync_ReturnsEmptyListWhenNoneExist()
    {
        var result = await _service.GetAttachmentsAsync(AttachmentEntityType.Project, 999);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetAttachmentsAsync_IncludesUploadedByUser()
    {
        await SeedAttachmentAsync("file.txt", AttachmentEntityType.Project, _project.Id);

        var result = await _service.GetAttachmentsAsync(AttachmentEntityType.Project, _project.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data![0].UploadedByUser);
        Assert.Equal("Test User", result.Data[0].UploadedByUser.DisplayName);
    }

    [Fact]
    public async Task GetAttachmentsAsync_OrderedByUploadedAt()
    {
        var a1 = new Attachment
        {
            FileName = "first.txt",
            StoredFileName = "s1.txt",
            ContentType = "text/plain",
            FileSizeBytes = 1,
            EntityType = AttachmentEntityType.Project,
            EntityId = _project.Id,
            UploadedByUserId = _user.Id,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        };

        var a2 = new Attachment
        {
            FileName = "second.txt",
            StoredFileName = "s2.txt",
            ContentType = "text/plain",
            FileSizeBytes = 1,
            EntityType = AttachmentEntityType.Project,
            EntityId = _project.Id,
            UploadedByUserId = _user.Id,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        };

        _context.Attachments.AddRange(a1, a2);
        await _context.SaveChangesAsync();

        var result = await _service.GetAttachmentsAsync(AttachmentEntityType.Project, _project.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("first.txt", result.Data![0].FileName);
        Assert.Equal("second.txt", result.Data[1].FileName);
    }

    // --- GetByIdAsync ---

    [Fact]
    public async Task GetByIdAsync_ReturnsAttachment()
    {
        var attachment = await SeedAttachmentAsync("found.txt", AttachmentEntityType.Project, _project.Id);

        var result = await _service.GetByIdAsync(attachment.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("found.txt", result.Data!.FileName);
        Assert.NotNull(result.Data.UploadedByUser);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsFailureWhenNotFound()
    {
        var result = await _service.GetByIdAsync(999);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors[0]);
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_RemovesAttachmentAndFile()
    {
        var attachment = await SeedAttachmentAsync("delete-me.txt", AttachmentEntityType.Project, _project.Id);

        var result = await _service.DeleteAsync(attachment.Id, _user.Id);

        Assert.True(result.Succeeded);
        Assert.Null(await _context.Attachments.FindAsync(attachment.Id));
        Assert.Contains(attachment.StoredFileName, _fileStorage.DeletedFiles);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailureWhenNotFound()
    {
        var result = await _service.DeleteAsync(999, _user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors[0]);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailureWhenFileDeleteFails()
    {
        var attachment = await SeedAttachmentAsync("fail.txt", AttachmentEntityType.Project, _project.Id);
        _fileStorage.ShouldFail = true;

        var result = await _service.DeleteAsync(attachment.Id, _user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Failed to delete file", result.Errors[0]);
        Assert.NotNull(await _context.Attachments.FindAsync(attachment.Id));
    }

    // --- Helpers ---

    private async Task<Attachment> SeedAttachmentAsync(string fileName, AttachmentEntityType entityType, int entityId)
    {
        var attachment = new Attachment
        {
            FileName = fileName,
            StoredFileName = $"{Guid.NewGuid()}.txt",
            ContentType = "text/plain",
            FileSizeBytes = 100,
            EntityType = entityType,
            EntityId = entityId,
            UploadedByUserId = _user.Id
        };

        _context.Attachments.Add(attachment);
        await _context.SaveChangesAsync();
        return attachment;
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private class InMemoryFileStorageService : IFileStorageService
    {
        public List<string> SavedFiles { get; } = [];
        public List<string> DeletedFiles { get; } = [];
        public bool ShouldFail { get; set; }

        public Task SaveFileAsync(Stream stream, string storedFileName)
        {
            if (ShouldFail)
                throw new IOException("Simulated save failure.");

            SavedFiles.Add(storedFileName);
            return Task.CompletedTask;
        }

        public Task<Stream> GetFileStreamAsync(string storedFileName)
        {
            if (ShouldFail)
                throw new IOException("Simulated read failure.");

            Stream stream = new MemoryStream("test content"u8.ToArray());
            return Task.FromResult(stream);
        }

        public Task DeleteFileAsync(string storedFileName)
        {
            if (ShouldFail)
                throw new IOException("Simulated delete failure.");

            DeletedFiles.Add(storedFileName);
            return Task.CompletedTask;
        }
    }
}
