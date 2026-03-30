using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

/// <summary>
/// Manages CRUD operations for server-side agent configuration.
/// All operations validate that the target user is an agent (IsAgent = true).
/// Secrets are encrypted at rest and only decrypted for MCP tool responses.
/// </summary>
public interface IAgentConfigurationService
{
    /// <summary>Gets the configuration with secrets masked for display. Returns null data if no config exists.</summary>
    Task<ServiceResult<AgentConfigurationDto?>> GetConfigurationAsync(string userId);

    /// <summary>Creates or updates the behavioral configuration fields for an agent.</summary>
    Task<ServiceResult> SaveConfigurationAsync(string userId, SaveAgentConfigurationDto dto);

    /// <summary>Adds a repository mapping. Returns the new repository ID.</summary>
    Task<ServiceResult<int>> AddRepositoryAsync(string userId, SaveAgentRepositoryDto dto);

    /// <summary>Updates an existing repository mapping by ID.</summary>
    Task<ServiceResult> UpdateRepositoryAsync(int repositoryId, SaveAgentRepositoryDto dto);

    /// <summary>Removes a repository mapping by ID.</summary>
    Task<ServiceResult> RemoveRepositoryAsync(int repositoryId);

    /// <summary>Adds an MCP server connection. Returns the new server ID.</summary>
    Task<ServiceResult<int>> AddMcpServerAsync(string userId, SaveAgentMcpServerDto dto);

    /// <summary>Updates an existing MCP server connection by ID.</summary>
    Task<ServiceResult> UpdateMcpServerAsync(int mcpServerId, SaveAgentMcpServerDto dto);

    /// <summary>Removes an MCP server connection by ID.</summary>
    Task<ServiceResult> RemoveMcpServerAsync(int mcpServerId);

    /// <summary>Gets the configuration with secrets fully decrypted. Used only for MCP tool responses to the agent.</summary>
    Task<ServiceResult<AgentConfigurationDto?>> GetDecryptedConfigurationAsync(string userId);
}
