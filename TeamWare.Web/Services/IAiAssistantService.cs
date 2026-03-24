namespace TeamWare.Web.Services;

public interface IAiAssistantService
{
    Task<ServiceResult<string>> RewriteProjectDescription(string description);

    Task<ServiceResult<string>> RewriteTaskDescription(string description);

    Task<ServiceResult<string>> PolishComment(string comment);

    Task<ServiceResult<string>> ExpandInboxItem(string title, string? description);

    Task<ServiceResult<string>> GenerateProjectSummary(int projectId, string userId, SummaryPeriod period);

    Task<ServiceResult<string>> GeneratePersonalDigest(string userId);

    Task<ServiceResult<string>> GenerateReviewPreparation(string userId);

    Task<bool> IsAvailable();
}
