using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class GlobalActivityServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly GlobalActivityService _service;

    public GlobalActivityServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new GlobalActivityService(_context);
    }

    private ApplicationUser CreateUser(string email, string displayName)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        return user;
    }

    private Project CreateProject(string name)
    {
        var project = new Project { Name = name };
        _context.Projects.Add(project);
        _context.SaveChanges();
        return project;
    }

    private void AddProjectMember(int projectId, string userId, ProjectRole role = ProjectRole.Member)
    {
        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role
        });
        _context.SaveChanges();
    }

    private TaskItem CreateTask(int projectId, string userId, string title)
    {
        var task = new TaskItem
        {
            Title = title,
            ProjectId = projectId,
            CreatedByUserId = userId,
            Status = TaskItemStatus.ToDo
        };
        _context.TaskItems.Add(task);
        _context.SaveChanges();
        return task;
    }

    private ActivityLogEntry CreateActivity(int taskId, int projectId, string userId,
        ActivityChangeType changeType, string? oldValue = null, string? newValue = null,
        DateTime? createdAt = null)
    {
        var entry = new ActivityLogEntry
        {
            TaskItemId = taskId,
            ProjectId = projectId,
            UserId = userId,
            ChangeType = changeType,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        _context.ActivityLogEntries.Add(entry);
        _context.SaveChanges();
        return entry;
    }

    // --- GetGlobalActivityFeed - Validation ---

    [Fact]
    public async Task GetGlobalActivityFeed_EmptyViewerId_ReturnsFailure()
    {
        var result = await _service.GetGlobalActivityFeed("");

        Assert.False(result.Succeeded);
        Assert.Contains("Viewer user ID is required", result.Errors[0]);
    }

    [Fact]
    public async Task GetGlobalActivityFeed_NullViewerId_ReturnsFailure()
    {
        var result = await _service.GetGlobalActivityFeed(null!);

        Assert.False(result.Succeeded);
    }

    // --- GetGlobalActivityFeed - Empty Results ---

    [Fact]
    public async Task GetGlobalActivityFeed_NoActivity_ReturnsEmptyList()
    {
        var viewer = CreateUser("viewer@test.com", "Viewer");

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    // --- GetGlobalActivityFeed - Full Detail for Member Projects (ACTV-03) ---

    [Fact]
    public async Task GetGlobalActivityFeed_MemberProject_ReturnsFullDetail()
    {
        var viewer = CreateUser("viewer@test.com", "Viewer");
        var actor = CreateUser("actor@test.com", "Actor");
        var project = CreateProject("My Project");
        AddProjectMember(project.Id, viewer.Id);
        AddProjectMember(project.Id, actor.Id);

        var task = CreateTask(project.Id, actor.Id, "Important Task");
        CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.Created);

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);

        var entry = result.Data![0];
        Assert.False(entry.IsMasked);
        Assert.Contains("Important Task", entry.Description);
        Assert.Equal("Actor", entry.UserDisplayName);
        Assert.Equal(actor.Id, entry.UserId);
        Assert.Equal("My Project", entry.ProjectName);
    }

    [Fact]
    public async Task GetGlobalActivityFeed_MemberProject_StatusChange_ShowsDetails()
    {
        var viewer = CreateUser("viewer2@test.com", "Viewer 2");
        var actor = CreateUser("actor2@test.com", "Actor 2");
        var project = CreateProject("Project A");
        AddProjectMember(project.Id, viewer.Id);

        var task = CreateTask(project.Id, actor.Id, "Task X");
        CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.StatusChanged, "ToDo", "InProgress");

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.True(result.Succeeded);
        var entry = result.Data![0];
        Assert.False(entry.IsMasked);
        Assert.Contains("Task X", entry.Description);
        Assert.Contains("ToDo", entry.Description);
        Assert.Contains("InProgress", entry.Description);
    }

    // --- GetGlobalActivityFeed - Masked for Non-Member Projects (ACTV-04) ---

    [Fact]
    public async Task GetGlobalActivityFeed_NonMemberProject_ReturnsMaskedDetail()
    {
        var viewer = CreateUser("viewer3@test.com", "Viewer 3");
        var actor = CreateUser("actor3@test.com", "Actor 3");
        var project = CreateProject("Secret Project");
        // Viewer is NOT a member of this project

        var task = CreateTask(project.Id, actor.Id, "Secret Task");
        CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.Created);

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);

        var entry = result.Data![0];
        Assert.True(entry.IsMasked);
        Assert.DoesNotContain("Secret Task", entry.Description);
        Assert.Equal("A user", entry.UserDisplayName);
        Assert.Equal(string.Empty, entry.UserId);
        Assert.Equal("Secret Project", entry.ProjectName);
    }

    [Fact]
    public async Task GetGlobalActivityFeed_NonMemberProject_MaskedDescriptions_AllChangeTypes()
    {
        var viewer = CreateUser("viewer4@test.com", "Viewer 4");
        var actor = CreateUser("actor4@test.com", "Actor 4");
        var project = CreateProject("Other Project");

        var task = CreateTask(project.Id, actor.Id, "Private Task");

        var changeTypes = new[]
        {
            ActivityChangeType.Created,
            ActivityChangeType.StatusChanged,
            ActivityChangeType.PriorityChanged,
            ActivityChangeType.Assigned,
            ActivityChangeType.Unassigned,
            ActivityChangeType.Updated,
            ActivityChangeType.Deleted
        };

        foreach (var ct in changeTypes)
        {
            CreateActivity(task.Id, project.Id, actor.Id, ct);
        }

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(changeTypes.Length, result.Data!.Count);

        foreach (var entry in result.Data!)
        {
            Assert.True(entry.IsMasked);
            Assert.DoesNotContain("Private Task", entry.Description);
        }
    }

    // --- GetGlobalActivityFeed - Mixed Projects ---

    [Fact]
    public async Task GetGlobalActivityFeed_MixedProjects_CorrectMasking()
    {
        var viewer = CreateUser("viewer5@test.com", "Viewer 5");
        var actor = CreateUser("actor5@test.com", "Actor 5");

        var memberProject = CreateProject("Member Project");
        AddProjectMember(memberProject.Id, viewer.Id);

        var otherProject = CreateProject("Other Project");

        var task1 = CreateTask(memberProject.Id, actor.Id, "Visible Task");
        var task2 = CreateTask(otherProject.Id, actor.Id, "Hidden Task");

        CreateActivity(task1.Id, memberProject.Id, actor.Id, ActivityChangeType.Created,
            createdAt: DateTime.UtcNow);
        CreateActivity(task2.Id, otherProject.Id, actor.Id, ActivityChangeType.Created,
            createdAt: DateTime.UtcNow.AddMinutes(-1));

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);

        var memberEntry = result.Data!.First(e => e.ProjectName == "Member Project");
        Assert.False(memberEntry.IsMasked);
        Assert.Contains("Visible Task", memberEntry.Description);

        var otherEntry = result.Data!.First(e => e.ProjectName == "Other Project");
        Assert.True(otherEntry.IsMasked);
        Assert.DoesNotContain("Hidden Task", otherEntry.Description);
    }

    // --- GetGlobalActivityFeed - Ordering ---

    [Fact]
    public async Task GetGlobalActivityFeed_ReturnsEntriesOrderedByDateDescending()
    {
        var viewer = CreateUser("viewer6@test.com", "Viewer 6");
        var actor = CreateUser("actor6@test.com", "Actor 6");
        var project = CreateProject("Project B");
        AddProjectMember(project.Id, viewer.Id);

        var task = CreateTask(project.Id, actor.Id, "Task");
        CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.Created,
            createdAt: DateTime.UtcNow.AddHours(-2));
        CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.StatusChanged,
            "ToDo", "InProgress", createdAt: DateTime.UtcNow.AddHours(-1));
        CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.Updated,
            createdAt: DateTime.UtcNow);

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.Count);

        // Most recent first
        Assert.True(result.Data![0].CreatedAt >= result.Data![1].CreatedAt);
        Assert.True(result.Data![1].CreatedAt >= result.Data![2].CreatedAt);
    }

    // --- GetGlobalActivityFeed - Count Limit ---

    [Fact]
    public async Task GetGlobalActivityFeed_RespectsCountLimit()
    {
        var viewer = CreateUser("viewer7@test.com", "Viewer 7");
        var actor = CreateUser("actor7@test.com", "Actor 7");
        var project = CreateProject("Project C");
        AddProjectMember(project.Id, viewer.Id);

        var task = CreateTask(project.Id, actor.Id, "Task");

        for (int i = 0; i < 10; i++)
        {
            CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.Updated,
                createdAt: DateTime.UtcNow.AddMinutes(-i));
        }

        var result = await _service.GetGlobalActivityFeed(viewer.Id, count: 5);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Data!.Count);
    }

    // --- GetGlobalActivityFeed - Excludes Old Activity ---

    [Fact]
    public async Task GetGlobalActivityFeed_ExcludesActivityOlderThan30Days()
    {
        var viewer = CreateUser("viewer8@test.com", "Viewer 8");
        var actor = CreateUser("actor8@test.com", "Actor 8");
        var project = CreateProject("Project D");
        AddProjectMember(project.Id, viewer.Id);

        var task = CreateTask(project.Id, actor.Id, "Task");

        CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.Created,
            createdAt: DateTime.UtcNow.AddDays(-31));
        CreateActivity(task.Id, project.Id, actor.Id, ActivityChangeType.Updated,
            createdAt: DateTime.UtcNow.AddDays(-1));

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
    }

    // --- GetGlobalActivityFeed - Full Detail Descriptions ---

    [Fact]
    public async Task GetGlobalActivityFeed_FullDetail_Created()
    {
        var viewer = CreateUser("viewer9@test.com", "Viewer 9");
        var project = CreateProject("Project E");
        AddProjectMember(project.Id, viewer.Id);

        var task = CreateTask(project.Id, viewer.Id, "My Task");
        CreateActivity(task.Id, project.Id, viewer.Id, ActivityChangeType.Created);

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.Contains("Created task", result.Data![0].Description);
        Assert.Contains("My Task", result.Data![0].Description);
    }

    [Fact]
    public async Task GetGlobalActivityFeed_FullDetail_PriorityChanged()
    {
        var viewer = CreateUser("viewer10@test.com", "Viewer 10");
        var project = CreateProject("Project F");
        AddProjectMember(project.Id, viewer.Id);

        var task = CreateTask(project.Id, viewer.Id, "Priority Task");
        CreateActivity(task.Id, project.Id, viewer.Id, ActivityChangeType.PriorityChanged, "Low", "High");

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.Contains("priority", result.Data![0].Description.ToLower());
        Assert.Contains("Low", result.Data![0].Description);
        Assert.Contains("High", result.Data![0].Description);
    }

    [Fact]
    public async Task GetGlobalActivityFeed_FullDetail_Assigned()
    {
        var viewer = CreateUser("viewer11@test.com", "Viewer 11");
        var project = CreateProject("Project G");
        AddProjectMember(project.Id, viewer.Id);

        var task = CreateTask(project.Id, viewer.Id, "Assigned Task");
        CreateActivity(task.Id, project.Id, viewer.Id, ActivityChangeType.Assigned);

        var result = await _service.GetGlobalActivityFeed(viewer.Id);

        Assert.Contains("Assigned", result.Data![0].Description);
    }

    // --- GetGlobalActivityFeed - Count Bounds ---

    [Fact]
    public async Task GetGlobalActivityFeed_NegativeCount_DefaultsTo50()
    {
        var viewer = CreateUser("viewer12@test.com", "Viewer 12");

        var result = await _service.GetGlobalActivityFeed(viewer.Id, count: -5);

        Assert.True(result.Succeeded);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
