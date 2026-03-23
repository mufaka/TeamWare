using TeamWare.Web.Models;

namespace TeamWare.Web.Services;

public interface IGlobalConfigurationService
{
    Task<ServiceResult<List<GlobalConfiguration>>> GetAllAsync();

    Task<ServiceResult<GlobalConfiguration>> GetByKeyAsync(string key);

    Task<ServiceResult> UpdateAsync(string key, string value, string userId);
}
