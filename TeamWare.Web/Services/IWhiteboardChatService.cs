using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public interface IWhiteboardChatService
{
    Task<ServiceResult<WhiteboardChatMessageDto>> SendMessageAsync(int whiteboardId, string userId, string content);

    Task<ServiceResult<List<WhiteboardChatMessageDto>>> GetMessagesAsync(int whiteboardId, int page, int pageSize);
}
