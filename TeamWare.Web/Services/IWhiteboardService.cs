using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public interface IWhiteboardService
{
    Task<ServiceResult<int>> CreateAsync(string userId, string title);

    Task<ServiceResult<WhiteboardDetailDto?>> GetByIdAsync(int whiteboardId);

    Task<ServiceResult<List<WhiteboardDto>>> GetLandingPageAsync(string userId, bool isSiteAdmin);

    Task<ServiceResult> DeleteAsync(int whiteboardId, string userId, bool isSiteAdmin);

    Task<ServiceResult> SaveCanvasAsync(int whiteboardId, string canvasData, string presenterId);

    Task<ServiceResult<bool>> CanAccessAsync(int whiteboardId, string userId, bool isSiteAdmin);
}
