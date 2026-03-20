using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class TaskServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly TaskService _taskService;
    private readonly ProjectService _projectService;
    private readonly ActivityLogService _activityLogService;
    private readonly NotificationService _notificationService;

    public TaskServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _activityLogService = new ActivityLogService(_context);
        _notificationService = new NotificationService(_context);
        _taskService = new TaskService(_context, _activityLogService, _notificationService);
        _projectService = new ProjectService(_context);
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

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner(string projectName = "Test Project")
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject(projectName, null, owner.Id);
        return (result.Data!, owner);
    }

    private async Task<ApplicationUser> AddMemberToProject(int projectId, string ownerUserId, ProjectRole role = ProjectRole.Member)
    {
        var member = CreateUser($"member-{Guid.NewGuid():N}@test.com", "Member");
        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = member.Id,
            Role = role
        });
        await _context.SaveChangesAsync();
        return member;
    }

    // --- CreateTask ---

    [Fact]
    public async Task CreateTask_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var result = await _taskService.CreateTask(project.Id, "My Task", "Description",
            TaskItemPriority.High, DateTime.UtcNow.AddDays(7), owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("My Task", result.Data.Title);
        Assert.Equal(TaskItemPriority.High, result.Data.Priority);
        Assert.Equal(TaskItemStatus.ToDo, result.Data.Status);
    }

    [Fact]
    public async Task CreateTask_EmptyTitle_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var result = await _taskService.CreateTask(project.Id, "", null,
            TaskItemPriority.Medium, null, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("title is required"));
    }

    [Fact]
    public async Task CreateTask_NonMember_Fails()
    {
        var (project, _) = await CreateProjectWithOwner();
        var outsider = CreateUser("outsider@test.com", "Outsider");

        var result = await _taskService.CreateTask(project.Id, "Task", null,
            TaskItemPriority.Medium, null, outsider.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("project member"));
    }

    [Fact]
    public async Task CreateTask_TrimsTitle()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var result = await _taskService.CreateTask(project.Id, "  Trimmed  ", null,
            TaskItemPriority.Medium, null, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Trimmed", result.Data!.Title);
    }

    // --- UpdateTask ---

    [Fact]
    public async Task UpdateTask_AsMember_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Original", null,
            TaskItemPriority.Low, null, owner.Id);

        var result = await _taskService.UpdateTask(create.Data!.Id, "Updated", "Desc",
            TaskItemPriority.Critical, DateTime.UtcNow.AddDays(3), owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.Data!.Title);
        Assert.Equal(TaskItemPriority.Critical, result.Data.Priority);
    }

    [Fact]
    public async Task UpdateTask_NonMember_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Task", null,
            TaskItemPriority.Medium, null, owner.Id);
        var outsider = CreateUser("outsider2@test.com", "Outsider");

        var result = await _taskService.UpdateTask(create.Data!.Id, "Hacked", null,
            TaskItemPriority.Medium, null, outsider.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateTask_NotFound_Fails()
    {
        var user = CreateUser("nf@test.com", "NotFound");

        var result = await _taskService.UpdateTask(999, "Title", null,
            TaskItemPriority.Medium, null, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    // --- DeleteTask ---

    [Fact]
    public async Task DeleteTask_AsOwner_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Delete Me", null,
            TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.DeleteTask(create.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);
        var task = await _context.TaskItems.FindAsync(create.Data.Id);
        Assert.Null(task);
    }

    [Fact]
    public async Task DeleteTask_AsAdmin_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var admin = await AddMemberToProject(project.Id, owner.Id, ProjectRole.Admin);
        var create = await _taskService.CreateTask(project.Id, "Admin Delete", null,
            TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.DeleteTask(create.Data!.Id, admin.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteTask_AsMember_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddMemberToProject(project.Id, owner.Id);
        var create = await _taskService.CreateTask(project.Id, "No Delete", null,
            TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.DeleteTask(create.Data!.Id, member.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("owners and admins"));
    }

    // --- ChangeStatus ---

    [Fact]
    public async Task ChangeStatus_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Status Task", null,
            TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.ChangeStatus(create.Data!.Id, TaskItemStatus.InProgress, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(TaskItemStatus.InProgress, result.Data!.Status);
    }

    [Fact]
    public async Task ChangeStatus_NonMember_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Status Task", null,
            TaskItemPriority.Medium, null, owner.Id);
        var outsider = CreateUser("outsider3@test.com", "Outsider");

        var result = await _taskService.ChangeStatus(create.Data!.Id, TaskItemStatus.Done, outsider.Id);

        Assert.False(result.Succeeded);
    }

    // --- AssignMembers / UnassignMembers ---

    [Fact]
    public async Task AssignMembers_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddMemberToProject(project.Id, owner.Id);
        var create = await _taskService.CreateTask(project.Id, "Assign Task", null,
            TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.AssignMembers(create.Data!.Id, [member.Id], owner.Id);

        Assert.True(result.Succeeded);
        var assignments = await _context.TaskAssignments
            .Where(a => a.TaskItemId == create.Data.Id)
            .ToListAsync();
        Assert.Single(assignments);
    }

    [Fact]
    public async Task AssignMembers_NonProjectMember_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var outsider = CreateUser("assign-outsider@test.com", "Outsider");
        var create = await _taskService.CreateTask(project.Id, "Task", null,
            TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.AssignMembers(create.Data!.Id, [outsider.Id], owner.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AssignMembers_DuplicateIgnored()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Dup Task", null,
            TaskItemPriority.Medium, null, owner.Id);

        await _taskService.AssignMembers(create.Data!.Id, [owner.Id], owner.Id);
        var result = await _taskService.AssignMembers(create.Data.Id, [owner.Id], owner.Id);

        Assert.True(result.Succeeded);
        var count = await _context.TaskAssignments
            .CountAsync(a => a.TaskItemId == create.Data.Id && a.UserId == owner.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UnassignMembers_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddMemberToProject(project.Id, owner.Id);
        var create = await _taskService.CreateTask(project.Id, "Unassign Task", null,
            TaskItemPriority.Medium, null, owner.Id);

        await _taskService.AssignMembers(create.Data!.Id, [member.Id], owner.Id);
        var result = await _taskService.UnassignMembers(create.Data.Id, [member.Id], owner.Id);

        Assert.True(result.Succeeded);
        var assignments = await _context.TaskAssignments
            .Where(a => a.TaskItemId == create.Data.Id)
            .ToListAsync();
        Assert.Empty(assignments);
    }

    // --- GTD: NextAction / SomedayMaybe ---

    [Fact]
    public async Task MarkAsNextAction_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Next Action", null,
            TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.MarkAsNextAction(create.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.IsNextAction);
        Assert.False(result.Data.IsSomedayMaybe);
    }

    [Fact]
    public async Task MarkAsNextAction_ClearsSomedayMaybe()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "SM to NA", null,
            TaskItemPriority.Medium, null, owner.Id);

        await _taskService.MarkAsSomedayMaybe(create.Data!.Id, owner.Id);
        var result = await _taskService.MarkAsNextAction(create.Data.Id, owner.Id);

        Assert.True(result.Data!.IsNextAction);
        Assert.False(result.Data.IsSomedayMaybe);
    }

    [Fact]
    public async Task ClearNextAction_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Clear NA", null,
            TaskItemPriority.Medium, null, owner.Id);

        await _taskService.MarkAsNextAction(create.Data!.Id, owner.Id);
        var result = await _taskService.ClearNextAction(create.Data.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.IsNextAction);
    }

    [Fact]
    public async Task MarkAsSomedayMaybe_ClearsNextAction()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "NA to SM", null,
            TaskItemPriority.Medium, null, owner.Id);

        await _taskService.MarkAsNextAction(create.Data!.Id, owner.Id);
        var result = await _taskService.MarkAsSomedayMaybe(create.Data.Id, owner.Id);

        Assert.False(result.Data!.IsNextAction);
        Assert.True(result.Data.IsSomedayMaybe);
    }

    [Fact]
    public async Task ClearSomedayMaybe_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Clear SM", null,
            TaskItemPriority.Medium, null, owner.Id);

        await _taskService.MarkAsSomedayMaybe(create.Data!.Id, owner.Id);
        var result = await _taskService.ClearSomedayMaybe(create.Data.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.IsSomedayMaybe);
    }

    // --- GetTasksForProject ---

    [Fact]
    public async Task GetTasksForProject_ReturnsAllTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Task 1", null, TaskItemPriority.Low, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Task 2", null, TaskItemPriority.High, null, owner.Id);

        var result = await _taskService.GetTasksForProject(project.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetTasksForProject_FilterByStatus()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var t1 = await _taskService.CreateTask(project.Id, "ToDo", null, TaskItemPriority.Medium, null, owner.Id);
        var t2 = await _taskService.CreateTask(project.Id, "Done", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.ChangeStatus(t2.Data!.Id, TaskItemStatus.Done, owner.Id);

        var result = await _taskService.GetTasksForProject(project.Id, owner.Id, statusFilter: TaskItemStatus.ToDo);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("ToDo", result.Data![0].Title);
    }

    [Fact]
    public async Task GetTasksForProject_FilterByPriority()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Low", null, TaskItemPriority.Low, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Critical", null, TaskItemPriority.Critical, null, owner.Id);

        var result = await _taskService.GetTasksForProject(project.Id, owner.Id, priorityFilter: TaskItemPriority.Critical);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Critical", result.Data![0].Title);
    }

    [Fact]
    public async Task GetTasksForProject_FilterByAssignee()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddMemberToProject(project.Id, owner.Id);

        var t1 = await _taskService.CreateTask(project.Id, "Assigned", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Unassigned", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.AssignMembers(t1.Data!.Id, [member.Id], owner.Id);

        var result = await _taskService.GetTasksForProject(project.Id, owner.Id, assigneeId: member.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Assigned", result.Data![0].Title);
    }

    [Fact]
    public async Task GetTasksForProject_NonMember_Fails()
    {
        var (project, _) = await CreateProjectWithOwner();
        var outsider = CreateUser("filter-outsider@test.com", "Outsider");

        var result = await _taskService.GetTasksForProject(project.Id, outsider.Id);

        Assert.False(result.Succeeded);
    }

    // --- SearchTasks ---

    [Fact]
    public async Task SearchTasks_ByTitle()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Build the API", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Design mockup", null, TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.SearchTasks(project.Id, "API", owner.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Build the API", result.Data![0].Title);
    }

    [Fact]
    public async Task SearchTasks_ByDescription()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Task A", "Contains database migration steps",
            TaskItemPriority.Medium, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Task B", "Frontend work",
            TaskItemPriority.Medium, null, owner.Id);

        var result = await _taskService.SearchTasks(project.Id, "migration", owner.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Task A", result.Data![0].Title);
    }

    [Fact]
    public async Task SearchTasks_EmptyQuery_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var result = await _taskService.SearchTasks(project.Id, "", owner.Id);

        Assert.False(result.Succeeded);
    }

    // --- GetTask ---

    [Fact]
    public async Task GetTask_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Detail Task", "Details here",
            TaskItemPriority.High, null, owner.Id);

        var result = await _taskService.GetTask(create.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Detail Task", result.Data!.Title);
        Assert.NotNull(result.Data.Project);
        Assert.NotNull(result.Data.CreatedBy);
    }

    [Fact]
    public async Task GetTask_NonMember_Fails()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var create = await _taskService.CreateTask(project.Id, "Task", null,
            TaskItemPriority.Medium, null, owner.Id);
        var outsider = CreateUser("get-outsider@test.com", "Outsider");

        var result = await _taskService.GetTask(create.Data!.Id, outsider.Id);

        Assert.False(result.Succeeded);
    }

    // --- GetWhatsNext ---

    [Fact]
    public async Task GetWhatsNext_ReturnsNextActionTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var t1 = await _taskService.CreateTask(project.Id, "Next", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Normal", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.MarkAsNextAction(t1.Data!.Id, owner.Id);

        var result = await _taskService.GetWhatsNext(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("Next", result.Data![0].Title);
    }

    [Fact]
    public async Task GetWhatsNext_ExcludesDoneTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var t1 = await _taskService.CreateTask(project.Id, "Done Next", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.MarkAsNextAction(t1.Data!.Id, owner.Id);
        await _taskService.ChangeStatus(t1.Data.Id, TaskItemStatus.Done, owner.Id);

        var result = await _taskService.GetWhatsNext(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetWhatsNext_ExcludesSomedayMaybe()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var t1 = await _taskService.CreateTask(project.Id, "SM Next", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.MarkAsNextAction(t1.Data!.Id, owner.Id);
        await _taskService.MarkAsSomedayMaybe(t1.Data.Id, owner.Id);

        var result = await _taskService.GetWhatsNext(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetWhatsNext_CrossProject()
    {
        var (p1, owner) = await CreateProjectWithOwner("Project 1");
        var p2Result = await _projectService.CreateProject("Project 2", null, owner.Id);

        var t1 = await _taskService.CreateTask(p1.Id, "P1 Next", null, TaskItemPriority.High, null, owner.Id);
        var t2 = await _taskService.CreateTask(p2Result.Data!.Id, "P2 Next", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.MarkAsNextAction(t1.Data!.Id, owner.Id);
        await _taskService.MarkAsNextAction(t2.Data!.Id, owner.Id);

        var result = await _taskService.GetWhatsNext(owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("P1 Next", result.Data[0].Title); // Higher priority first
    }

    [Fact]
    public async Task GetWhatsNext_RespectsLimit()
    {
        var (project, owner) = await CreateProjectWithOwner();
        for (int i = 0; i < 5; i++)
        {
            var t = await _taskService.CreateTask(project.Id, $"Task {i}", null,
                TaskItemPriority.High, null, owner.Id);
            await _taskService.MarkAsNextAction(t.Data!.Id, owner.Id);
        }

        var result = await _taskService.GetWhatsNext(owner.Id, limit: 3);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.Count);
    }

    // --- ProjectDashboard task counts ---

    [Fact]
    public async Task ProjectDashboard_ShowsRealTaskCounts()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var t1 = await _taskService.CreateTask(project.Id, "ToDo", null, TaskItemPriority.Medium, null, owner.Id);
        var t2 = await _taskService.CreateTask(project.Id, "InProgress", null, TaskItemPriority.Medium, null, owner.Id);
        var t3 = await _taskService.CreateTask(project.Id, "InReview", null, TaskItemPriority.Medium, null, owner.Id);
        var t4 = await _taskService.CreateTask(project.Id, "Done", null, TaskItemPriority.Medium, null, owner.Id);

        await _taskService.ChangeStatus(t2.Data!.Id, TaskItemStatus.InProgress, owner.Id);
        await _taskService.ChangeStatus(t3.Data!.Id, TaskItemStatus.InReview, owner.Id);
        await _taskService.ChangeStatus(t4.Data!.Id, TaskItemStatus.Done, owner.Id);

        var dashboard = await _projectService.GetProjectDashboard(project.Id, owner.Id);

        Assert.True(dashboard.Succeeded);
        Assert.Equal(1, dashboard.Data!.TaskCountToDo);
        Assert.Equal(1, dashboard.Data.TaskCountInProgress);
        Assert.Equal(1, dashboard.Data.TaskCountInReview);
        Assert.Equal(1, dashboard.Data.TaskCountDone);
        Assert.Equal(4, dashboard.Data.TotalTasks);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
