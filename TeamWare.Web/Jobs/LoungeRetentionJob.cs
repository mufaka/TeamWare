using TeamWare.Web.Services;

namespace TeamWare.Web.Jobs;

public class LoungeRetentionJob
{
    private readonly ILoungeService _loungeService;
    private readonly ILogger<LoungeRetentionJob> _logger;

    public LoungeRetentionJob(ILoungeService loungeService, ILogger<LoungeRetentionJob> logger)
    {
        _loungeService = loungeService;
        _logger = logger;
    }

    public async Task Execute()
    {
        _logger.LogInformation("Lounge retention job started.");

        var deletedCount = await _loungeService.CleanupExpiredMessages();

        _logger.LogInformation("Lounge retention job completed. Deleted {DeletedCount} expired messages.", deletedCount);
    }
}
