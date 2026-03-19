using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IProjectService
{
    Task<ServiceResult<Project>> CreateProject(string name, string? description, string creatorUserId);

    Task<ServiceResult<Project>> UpdateProject(int projectId, string name, string? description, string userId);

    Task<ServiceResult> ArchiveProject(int projectId, string userId);

    Task<ServiceResult> DeleteProject(int projectId, string userId);

    Task<ServiceResult<List<Project>>> GetProjectsForUser(string userId);

    Task<ServiceResult<ProjectDashboard>> GetProjectDashboard(int projectId, string userId);
}
