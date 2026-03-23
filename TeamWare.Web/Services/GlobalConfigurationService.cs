using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public class GlobalConfigurationService : IGlobalConfigurationService
{
    private readonly ApplicationDbContext _context;
    private readonly IAdminActivityLogService _activityLogService;

    public GlobalConfigurationService(
        ApplicationDbContext context,
        IAdminActivityLogService activityLogService)
    {
        _context = context;
        _activityLogService = activityLogService;
    }

    public async Task<ServiceResult<List<GlobalConfiguration>>> GetAllAsync()
    {
        var configs = await _context.GlobalConfigurations
            .Include(gc => gc.UpdatedByUser)
            .OrderBy(gc => gc.Key)
            .ToListAsync();

        return ServiceResult<List<GlobalConfiguration>>.Success(configs);
    }

    public async Task<ServiceResult<GlobalConfiguration>> GetByKeyAsync(string key)
    {
        var config = await _context.GlobalConfigurations
            .Include(gc => gc.UpdatedByUser)
            .FirstOrDefaultAsync(gc => gc.Key == key);

        if (config == null)
        {
            return ServiceResult<GlobalConfiguration>.Failure($"Configuration key '{key}' not found.");
        }

        return ServiceResult<GlobalConfiguration>.Success(config);
    }

    public async Task<ServiceResult> UpdateAsync(string key, string value, string userId)
    {
        var config = await _context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == key);

        if (config == null)
        {
            return ServiceResult.Failure($"Configuration key '{key}' not found.");
        }

        var oldValue = config.Value;
        config.Value = value;
        config.UpdatedAt = DateTime.UtcNow;
        config.UpdatedByUserId = userId;

        await _context.SaveChangesAsync();

        await _activityLogService.LogAction(
            userId,
            "UpdateConfiguration",
            details: $"Changed '{key}' from '{oldValue}' to '{value}'");

        return ServiceResult.Success();
    }
}
