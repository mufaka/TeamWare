using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IAttachmentService
{
    Task<ServiceResult<List<Attachment>>> GetAttachmentsAsync(AttachmentEntityType entityType, int entityId);

    Task<ServiceResult<Attachment>> GetByIdAsync(int id);

    Task<ServiceResult<Attachment>> UploadAsync(Stream fileStream, string fileName, string contentType, long fileSizeBytes, AttachmentEntityType entityType, int entityId, string userId);

    Task<ServiceResult> DeleteAsync(int id, string userId);
}
