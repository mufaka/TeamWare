using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class AttachmentEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;
    private readonly Project _project;

    public AttachmentEntityTests()
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
            UserName = "uploader@test.com",
            Email = "uploader@test.com",
            DisplayName = "Uploader"
        };

        _context.Users.Add(_user);

        _project = new Project { Name = "Test Project" };
        _context.Projects.Add(_project);

        _context.SaveChanges();
    }

    [Fact]
    public async Task CanCreateAttachment()
    {
        var attachment = new Attachment
        {
            FileName = "report.pdf",
            StoredFileName = "a1b2c3d4-e5f6-7890-abcd-ef1234567890.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            EntityType = AttachmentEntityType.Project,
            EntityId = _project.Id,
            UploadedByUserId = _user.Id
        };

        _context.Attachments.Add(attachment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Attachments.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal("report.pdf", retrieved.FileName);
        Assert.Equal("application/pdf", retrieved.ContentType);
        Assert.Equal(1024, retrieved.FileSizeBytes);
        Assert.Equal(AttachmentEntityType.Project, retrieved.EntityType);
        Assert.Equal(_project.Id, retrieved.EntityId);
    }

    [Fact]
    public async Task Attachment_HasTimestamp()
    {
        var before = DateTime.UtcNow;

        var attachment = new Attachment
        {
            FileName = "doc.txt",
            StoredFileName = "stored.txt",
            ContentType = "text/plain",
            FileSizeBytes = 100,
            EntityType = AttachmentEntityType.Comment,
            EntityId = 1,
            UploadedByUserId = _user.Id
        };

        _context.Attachments.Add(attachment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Attachments.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.UploadedAt >= before);
        Assert.True(retrieved.UploadedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task Attachment_CanLoadUploadedByUser()
    {
        var attachment = new Attachment
        {
            FileName = "image.png",
            StoredFileName = "stored.png",
            ContentType = "image/png",
            FileSizeBytes = 2048,
            EntityType = AttachmentEntityType.LoungeMessage,
            EntityId = 1,
            UploadedByUserId = _user.Id
        };

        _context.Attachments.Add(attachment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Attachments
            .Include(a => a.UploadedByUser)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.UploadedByUser);
        Assert.Equal("Uploader", retrieved.UploadedByUser.DisplayName);
    }

    [Fact]
    public async Task Attachment_EntityTypeStoredAsString()
    {
        var attachment = new Attachment
        {
            FileName = "file.zip",
            StoredFileName = "stored.zip",
            ContentType = "application/zip",
            FileSizeBytes = 4096,
            EntityType = AttachmentEntityType.Project,
            EntityId = _project.Id,
            UploadedByUserId = _user.Id
        };

        _context.Attachments.Add(attachment);
        await _context.SaveChangesAsync();

        // Query raw to verify string storage
        var rawValue = await _context.Database
            .SqlQueryRaw<string>("SELECT EntityType AS Value FROM Attachments LIMIT 1")
            .FirstOrDefaultAsync();

        Assert.Equal("Project", rawValue);
    }

    [Fact]
    public async Task Attachment_MultiplePerEntity()
    {
        _context.Attachments.Add(new Attachment
        {
            FileName = "file1.txt",
            StoredFileName = "stored1.txt",
            ContentType = "text/plain",
            FileSizeBytes = 100,
            EntityType = AttachmentEntityType.Project,
            EntityId = _project.Id,
            UploadedByUserId = _user.Id
        });

        _context.Attachments.Add(new Attachment
        {
            FileName = "file2.txt",
            StoredFileName = "stored2.txt",
            ContentType = "text/plain",
            FileSizeBytes = 200,
            EntityType = AttachmentEntityType.Project,
            EntityId = _project.Id,
            UploadedByUserId = _user.Id
        });

        await _context.SaveChangesAsync();

        var attachments = await _context.Attachments
            .Where(a => a.EntityType == AttachmentEntityType.Project && a.EntityId == _project.Id)
            .ToListAsync();

        Assert.Equal(2, attachments.Count);
    }

    [Fact]
    public async Task Attachment_DifferentEntityTypes()
    {
        _context.Attachments.Add(new Attachment
        {
            FileName = "project.txt",
            StoredFileName = "s1.txt",
            ContentType = "text/plain",
            FileSizeBytes = 100,
            EntityType = AttachmentEntityType.Project,
            EntityId = 1,
            UploadedByUserId = _user.Id
        });

        _context.Attachments.Add(new Attachment
        {
            FileName = "comment.txt",
            StoredFileName = "s2.txt",
            ContentType = "text/plain",
            FileSizeBytes = 200,
            EntityType = AttachmentEntityType.Comment,
            EntityId = 1,
            UploadedByUserId = _user.Id
        });

        _context.Attachments.Add(new Attachment
        {
            FileName = "lounge.txt",
            StoredFileName = "s3.txt",
            ContentType = "text/plain",
            FileSizeBytes = 300,
            EntityType = AttachmentEntityType.LoungeMessage,
            EntityId = 1,
            UploadedByUserId = _user.Id
        });

        await _context.SaveChangesAsync();

        var projectAttachments = await _context.Attachments
            .Where(a => a.EntityType == AttachmentEntityType.Project)
            .ToListAsync();
        var commentAttachments = await _context.Attachments
            .Where(a => a.EntityType == AttachmentEntityType.Comment)
            .ToListAsync();
        var loungeAttachments = await _context.Attachments
            .Where(a => a.EntityType == AttachmentEntityType.LoungeMessage)
            .ToListAsync();

        Assert.Single(projectAttachments);
        Assert.Single(commentAttachments);
        Assert.Single(loungeAttachments);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
