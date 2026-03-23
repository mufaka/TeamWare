using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Web.Jobs;

public class LoungeRetentionJob
{
    private readonly ILoungeService _loungeService;
    private readonly IAttachmentService _attachmentService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<LoungeRetentionJob> _logger;

    public LoungeRetentionJob(
        ILoungeService loungeService,
        IAttachmentService attachmentService,
        IFileStorageService fileStorageService,
        ILogger<LoungeRetentionJob> logger)
    {
        _loungeService = loungeService;
        _attachmentService = attachmentService;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task Execute()
    {
        _logger.LogInformation("Lounge retention job started.");

        // Get message IDs that will be cleaned up so we can delete their attachments
        var expiredMessageIds = await _loungeService.GetExpiredMessageIds();

        // Delete attachment files for expired messages
        var attachmentsDeleted = 0;
        foreach (var messageId in expiredMessageIds)
        {
            var attachmentsResult = await _attachmentService.GetAttachmentsAsync(AttachmentEntityType.LoungeMessage, messageId);
            if (attachmentsResult.Succeeded && attachmentsResult.Data != null)
            {
                foreach (var attachment in attachmentsResult.Data)
                {
                    try
                    {
                        await _fileStorageService.DeleteFileAsync(attachment.StoredFileName);
                        await _attachmentService.DeleteAsync(attachment.Id, "system");
                        attachmentsDeleted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete attachment {AttachmentId} for expired message {MessageId}.", attachment.Id, messageId);
                    }
                }
            }
        }

        var deletedCount = await _loungeService.CleanupExpiredMessages();

        _logger.LogInformation("Lounge retention job completed. Deleted {DeletedCount} expired messages and {AttachmentsDeleted} attachments.", deletedCount, attachmentsDeleted);
    }
}
