using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public interface IGlobalActivityService
{
    Task<ServiceResult<List<GlobalActivityFeedEntryViewModel>>> GetGlobalActivityFeed(
        string viewerUserId, int count = 50);
}
