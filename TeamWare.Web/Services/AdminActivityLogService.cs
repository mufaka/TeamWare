using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class AdminActivityLogService : IAdminActivityLogService
{
    private readonly ApplicationDbContext _context;

    public AdminActivityLogService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAction(string adminUserId, string action, string? targetUserId = null, int? targetProjectId = null, string? details = null)
    {
        var entry = new AdminActivityLog
        {
            AdminUserId = adminUserId,
            Action = action,
            TargetUserId = targetUserId,
            TargetProjectId = targetProjectId,
            Details = details,
            CreatedAt = DateTime.UtcNow
        };

        _context.AdminActivityLogs.Add(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<ServiceResult<PagedResult<AdminActivityLog>>> GetActivityLog(int page, int pageSize)
    {
        if (page < 1)
        {
            return ServiceResult<PagedResult<AdminActivityLog>>.Failure("Page must be at least 1.");
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return ServiceResult<PagedResult<AdminActivityLog>>.Failure("Page size must be between 1 and 100.");
        }

        var totalCount = await _context.AdminActivityLogs.CountAsync();

        var items = await _context.AdminActivityLogs
            .Include(a => a.AdminUser)
            .Include(a => a.TargetUser)
            .Include(a => a.TargetProject)
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = new PagedResult<AdminActivityLog>(items, totalCount, page, pageSize);
        return ServiceResult<PagedResult<AdminActivityLog>>.Success(result);
    }
}
