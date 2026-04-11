namespace TeamWare.Web.Services;

public interface IWhiteboardProjectService
{
    Task<ServiceResult> SaveToProjectAsync(int whiteboardId, int projectId, string userId);

    Task<ServiceResult> ClearProjectAsync(int whiteboardId, string userId);

    Task<ServiceResult> TransferOwnershipIfNeededAsync(int whiteboardId);
}
