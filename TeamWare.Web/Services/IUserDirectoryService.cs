using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public interface IUserDirectoryService
{
    Task<ServiceResult<PagedResult<UserDirectoryEntryViewModel>>> SearchUsers(string? searchTerm, int page = 1, int pageSize = 20, string? userTypeFilter = null);

    Task<ServiceResult<PagedResult<UserDirectoryEntryViewModel>>> GetUsersSorted(string sortBy = "displayname", bool ascending = true, int page = 1, int pageSize = 20, string? userTypeFilter = null);

    Task<ServiceResult<UserProfileViewModel>> GetUserProfile(string userId);

    Task<ServiceResult<UserTaskStatisticsViewModel>> GetUserTaskStatistics(string userId);

    Task<ServiceResult<List<UserRecentActivityViewModel>>> GetUserRecentActivity(string userId);
}
