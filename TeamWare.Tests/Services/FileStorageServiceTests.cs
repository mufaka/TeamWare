using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class FileStorageServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly FileStorageService _service;
    private readonly string _tempDir;

    public FileStorageServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _tempDir = Path.Combine(Path.GetTempPath(), $"teamware_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = "ATTACHMENT_DIR",
            Value = _tempDir,
            Description = "Test attachment directory"
        });
        _context.SaveChanges();

        var activityLogService = new AdminActivityLogService(_context);
        var configService = new GlobalConfigurationService(_context, activityLogService, new MemoryCache(new MemoryCacheOptions()));
        _service = new FileStorageService(configService);
    }

    [Fact]
    public async Task SaveFileAsync_CreatesFileOnDisk()
    {
        var content = "hello world"u8.ToArray();
        using var stream = new MemoryStream(content);

        await _service.SaveFileAsync(stream, "test.txt");

        var filePath = Path.Combine(_tempDir, "test.txt");
        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllBytesAsync(filePath));
    }

    [Fact]
    public async Task GetFileStreamAsync_ReturnsFileContent()
    {
        var content = "file content"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "existing.txt"), content);

        using var stream = await _service.GetFileStreamAsync("existing.txt");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(content, ms.ToArray());
    }

    [Fact]
    public async Task GetFileStreamAsync_ThrowsWhenFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.GetFileStreamAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task DeleteFileAsync_RemovesFileFromDisk()
    {
        var filePath = Path.Combine(_tempDir, "todelete.txt");
        await File.WriteAllTextAsync(filePath, "content");
        Assert.True(File.Exists(filePath));

        await _service.DeleteFileAsync("todelete.txt");

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteFileAsync_DoesNotThrowWhenFileNotFound()
    {
        var exception = await Record.ExceptionAsync(
            () => _service.DeleteFileAsync("nonexistent.txt"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SaveFileAsync_CreatesDirectoryIfNotExists()
    {
        var subDir = Path.Combine(_tempDir, "subdir");

        _context.GlobalConfigurations.RemoveRange(_context.GlobalConfigurations);
        _context.GlobalConfigurations.Add(new GlobalConfiguration
        {
            Key = "ATTACHMENT_DIR",
            Value = subDir,
            Description = "Nested dir"
        });
        await _context.SaveChangesAsync();

        using var stream = new MemoryStream("data"u8.ToArray());
        await _service.SaveFileAsync(stream, "nested.txt");

        Assert.True(File.Exists(Path.Combine(subDir, "nested.txt")));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
