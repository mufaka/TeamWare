using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class AttachmentService : IAttachmentService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;

    public AttachmentService(ApplicationDbContext context, IFileStorageService fileStorageService)
    {
        _context = context;
        _fileStorageService = fileStorageService;
    }

    public async Task<ServiceResult<List<Attachment>>> GetAttachmentsAsync(AttachmentEntityType entityType, int entityId)
    {
        var attachments = await _context.Attachments
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderBy(a => a.UploadedAt)
            .Include(a => a.UploadedByUser)
            .ToListAsync();

        return ServiceResult<List<Attachment>>.Success(attachments);
    }

    public async Task<ServiceResult<Attachment>> GetByIdAsync(int id)
    {
        var attachment = await _context.Attachments
            .Include(a => a.UploadedByUser)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (attachment is null)
            return ServiceResult<Attachment>.Failure("Attachment not found.");

        return ServiceResult<Attachment>.Success(attachment);
    }

    public async Task<ServiceResult<Attachment>> UploadAsync(Stream fileStream, string fileName, string contentType, long fileSizeBytes, AttachmentEntityType entityType, int entityId, string userId)
    {
        var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(fileName)}";

        try
        {
            await _fileStorageService.SaveFileAsync(fileStream, storedFileName);
        }
        catch (Exception ex)
        {
            return ServiceResult<Attachment>.Failure($"Failed to save file: {ex.Message}");
        }

        var attachment = new Attachment
        {
            FileName = fileName,
            StoredFileName = storedFileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            EntityType = entityType,
            EntityId = entityId,
            UploadedByUserId = userId,
            UploadedAt = DateTime.UtcNow
        };

        _context.Attachments.Add(attachment);
        await _context.SaveChangesAsync();

        return ServiceResult<Attachment>.Success(attachment);
    }

    public async Task<ServiceResult> DeleteAsync(int id, string userId)
    {
        var attachment = await _context.Attachments.FindAsync(id);

        if (attachment is null)
            return ServiceResult.Failure("Attachment not found.");

        try
        {
            await _fileStorageService.DeleteFileAsync(attachment.StoredFileName);
        }
        catch (Exception ex)
        {
            return ServiceResult.Failure($"Failed to delete file: {ex.Message}");
        }

        _context.Attachments.Remove(attachment);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }
}
