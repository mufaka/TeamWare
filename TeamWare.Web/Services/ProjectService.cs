using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class ProjectService : IProjectService
{
    private readonly ApplicationDbContext _context;

    public ProjectService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<Project>> CreateProject(string name, string? description, string creatorUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<Project>.Failure("Project name is required.");
        }

        var project = new Project
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var ownerMember = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = creatorUserId,
            Role = ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        };

        _context.ProjectMembers.Add(ownerMember);
        await _context.SaveChangesAsync();

        return ServiceResult<Project>.Success(project);
    }

    public async Task<ServiceResult<Project>> UpdateProject(int projectId, string name, string? description, string userId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<Project>.Failure("Project name is required.");
        }

        var project = await _context.Projects.FindAsync(projectId);
        if (project == null)
        {
            return ServiceResult<Project>.Failure("Project not found.");
        }

        var membership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);

        if (membership == null || (membership.Role != ProjectRole.Owner && membership.Role != ProjectRole.Admin))
        {
            return ServiceResult<Project>.Failure("Only project owners and admins can edit project details.");
        }

        project.Name = name.Trim();
        project.Description = description?.Trim();
        project.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult<Project>.Success(project);
    }

    public async Task<ServiceResult> ArchiveProject(int projectId, string userId)
    {
        var project = await _context.Projects.FindAsync(projectId);
        if (project == null)
        {
            return ServiceResult.Failure("Project not found.");
        }

        var membership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);

        if (membership == null || membership.Role != ProjectRole.Owner)
        {
            return ServiceResult.Failure("Only project owners can archive a project.");
        }

        project.Status = ProjectStatus.Archived;
        project.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DeleteProject(int projectId, string userId)
    {
        var project = await _context.Projects.FindAsync(projectId);
        if (project == null)
        {
            return ServiceResult.Failure("Project not found.");
        }

        var membership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);

        if (membership == null || membership.Role != ProjectRole.Owner)
        {
            return ServiceResult.Failure("Only project owners can delete a project.");
        }

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<Project>>> GetProjectsForUser(string userId)
    {
        var projects = await _context.ProjectMembers
            .Where(pm => pm.UserId == userId)
            .Include(pm => pm.Project)
                .ThenInclude(p => p.Members)
            .Select(pm => pm.Project)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();

        return ServiceResult<List<Project>>.Success(projects);
    }

    public async Task<ServiceResult<ProjectDashboard>> GetProjectDashboard(int projectId, string userId)
    {
        var project = await _context.Projects
            .Include(p => p.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            return ServiceResult<ProjectDashboard>.Failure("Project not found.");
        }

        var isMember = project.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            return ServiceResult<ProjectDashboard>.Failure("You are not a member of this project.");
        }

        var dashboard = new ProjectDashboard
        {
            Project = project,
            TotalMembers = project.Members.Count,
            // Task counts will be populated in Phase 2 when TaskItem is implemented
            TaskCountToDo = 0,
            TaskCountInProgress = 0,
            TaskCountInReview = 0,
            TaskCountDone = 0
        };

        return ServiceResult<ProjectDashboard>.Success(dashboard);
    }
}
