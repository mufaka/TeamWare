using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class ProjectEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public ProjectEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task CanCreateProject()
    {
        var project = new Project
        {
            Name = "Test Project",
            Description = "A test project"
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Projects.FirstOrDefaultAsync(p => p.Name == "Test Project");
        Assert.NotNull(retrieved);
        Assert.Equal("Test Project", retrieved.Name);
        Assert.Equal("A test project", retrieved.Description);
        Assert.Equal(ProjectStatus.Active, retrieved.Status);
    }

    [Fact]
    public async Task Project_DefaultStatus_IsActive()
    {
        var project = new Project { Name = "Default Status Project" };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Projects.FirstOrDefaultAsync(p => p.Name == "Default Status Project");
        Assert.NotNull(retrieved);
        Assert.Equal(ProjectStatus.Active, retrieved.Status);
    }

    [Fact]
    public async Task Project_CanBeArchived()
    {
        var project = new Project
        {
            Name = "Archivable Project",
            Status = ProjectStatus.Archived
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Projects.FirstOrDefaultAsync(p => p.Name == "Archivable Project");
        Assert.NotNull(retrieved);
        Assert.Equal(ProjectStatus.Archived, retrieved.Status);
    }

    [Fact]
    public async Task Project_Name_IsRequired()
    {
        var project = new Project { Name = null! };

        _context.Projects.Add(project);

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task Project_Description_IsOptional()
    {
        var project = new Project
        {
            Name = "No Description Project",
            Description = null
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Projects.FirstOrDefaultAsync(p => p.Name == "No Description Project");
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Description);
    }

    [Fact]
    public async Task Project_HasMembersCollection()
    {
        var project = new Project { Name = "Team Project" };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Name == "Team Project");

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Members);
        Assert.Empty(retrieved.Members);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
