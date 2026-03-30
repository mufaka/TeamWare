using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

public interface IAgentConfigurationService
{
    Task<ServiceResult<AgentConfigurationDto?>> GetConfigurationAsync(string userId);

    Task<ServiceResult> SaveConfigurationAsync(string userId, SaveAgentConfigurationDto dto);

    Task<ServiceResult<int>> AddRepositoryAsync(string userId, SaveAgentRepositoryDto dto);

    Task<ServiceResult> UpdateRepositoryAsync(int repositoryId, SaveAgentRepositoryDto dto);

    Task<ServiceResult> RemoveRepositoryAsync(int repositoryId);

    Task<ServiceResult<int>> AddMcpServerAsync(string userId, SaveAgentMcpServerDto dto);

    Task<ServiceResult> UpdateMcpServerAsync(int mcpServerId, SaveAgentMcpServerDto dto);

    Task<ServiceResult> RemoveMcpServerAsync(int mcpServerId);

    Task<ServiceResult<AgentConfigurationDto?>> GetDecryptedConfigurationAsync(string userId);
}
