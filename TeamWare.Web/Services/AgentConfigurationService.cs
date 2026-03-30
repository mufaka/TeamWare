using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Services;

/// <summary>
/// Implements agent configuration CRUD with encrypted secret storage.
/// All operations validate that the target user has IsAgent = true.
/// </summary>
public class AgentConfigurationService : IAgentConfigurationService
{
    private readonly ApplicationDbContext _context;
    private readonly IAgentSecretEncryptor _encryptor;

    public AgentConfigurationService(ApplicationDbContext context, IAgentSecretEncryptor encryptor)
    {
        _context = context;
        _encryptor = encryptor;
    }

    public async Task<ServiceResult<AgentConfigurationDto?>> GetConfigurationAsync(string userId)
    {
        var validation = await ValidateAgentUserAsync(userId);
        if (!validation.Succeeded)
            return ServiceResult<AgentConfigurationDto?>.Failure(validation.Errors);

        var config = await LoadConfigurationAsync(userId);
        if (config == null)
            return ServiceResult<AgentConfigurationDto?>.Success(null);

        return ServiceResult<AgentConfigurationDto?>.Success(MapToDto(config, masked: true));
    }

    public async Task<ServiceResult<AgentConfigurationDto?>> GetDecryptedConfigurationAsync(string userId)
    {
        var validation = await ValidateAgentUserAsync(userId);
        if (!validation.Succeeded)
            return ServiceResult<AgentConfigurationDto?>.Failure(validation.Errors);

        var config = await LoadConfigurationAsync(userId);
        if (config == null)
            return ServiceResult<AgentConfigurationDto?>.Success(null);

        return ServiceResult<AgentConfigurationDto?>.Success(MapToDto(config, masked: false));
    }

    public async Task<ServiceResult> SaveConfigurationAsync(string userId, SaveAgentConfigurationDto dto)
    {
        var validation = await ValidateAgentUserAsync(userId);
        if (!validation.Succeeded)
            return validation;

        var config = await _context.AgentConfigurations
            .FirstOrDefaultAsync(ac => ac.UserId == userId);

        if (config == null)
        {
            config = new AgentConfiguration
            {
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _context.AgentConfigurations.Add(config);
        }

        config.PollingIntervalSeconds = dto.PollingIntervalSeconds;
        config.Model = dto.Model;
        config.AutoApproveTools = dto.AutoApproveTools;
        config.DryRun = dto.DryRun;
        config.TaskTimeoutSeconds = dto.TaskTimeoutSeconds;
        config.SystemPrompt = dto.SystemPrompt;
        config.RepositoryUrl = dto.RepositoryUrl;
        config.RepositoryBranch = dto.RepositoryBranch;
        config.EncryptedRepositoryAccessToken = _encryptor.Encrypt(dto.RepositoryAccessToken);
        config.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<int>> AddRepositoryAsync(string userId, SaveAgentRepositoryDto dto)
    {
        var validation = await ValidateAgentUserAsync(userId);
        if (!validation.Succeeded)
            return ServiceResult<int>.Failure(validation.Errors);

        var config = await EnsureConfigurationAsync(userId);

        var exists = await _context.AgentRepositories
            .AnyAsync(ar => ar.AgentConfigurationId == config.Id
                && ar.ProjectName == dto.ProjectName);
        if (exists)
            return ServiceResult<int>.Failure($"A repository with project name '{dto.ProjectName}' already exists.");

        var repo = new AgentRepository
        {
            AgentConfigurationId = config.Id,
            ProjectName = dto.ProjectName,
            Url = dto.Url,
            Branch = dto.Branch,
            EncryptedAccessToken = _encryptor.Encrypt(dto.AccessToken),
            DisplayOrder = dto.DisplayOrder
        };

        _context.AgentRepositories.Add(repo);
        config.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult<int>.Success(repo.Id);
    }

    public async Task<ServiceResult> UpdateRepositoryAsync(int repositoryId, SaveAgentRepositoryDto dto)
    {
        var repo = await _context.AgentRepositories
            .Include(ar => ar.AgentConfiguration)
            .FirstOrDefaultAsync(ar => ar.Id == repositoryId);

        if (repo == null)
            return ServiceResult.Failure("Repository not found.");

        var duplicateExists = await _context.AgentRepositories
            .AnyAsync(ar => ar.AgentConfigurationId == repo.AgentConfigurationId
                && ar.ProjectName == dto.ProjectName
                && ar.Id != repositoryId);
        if (duplicateExists)
            return ServiceResult.Failure($"A repository with project name '{dto.ProjectName}' already exists.");

        repo.ProjectName = dto.ProjectName;
        repo.Url = dto.Url;
        repo.Branch = dto.Branch;
        repo.EncryptedAccessToken = _encryptor.Encrypt(dto.AccessToken);
        repo.DisplayOrder = dto.DisplayOrder;
        repo.AgentConfiguration.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RemoveRepositoryAsync(int repositoryId)
    {
        var repo = await _context.AgentRepositories
            .Include(ar => ar.AgentConfiguration)
            .FirstOrDefaultAsync(ar => ar.Id == repositoryId);

        if (repo == null)
            return ServiceResult.Failure("Repository not found.");

        repo.AgentConfiguration.UpdatedAt = DateTime.UtcNow;
        _context.AgentRepositories.Remove(repo);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<int>> AddMcpServerAsync(string userId, SaveAgentMcpServerDto dto)
    {
        var validation = await ValidateAgentUserAsync(userId);
        if (!validation.Succeeded)
            return ServiceResult<int>.Failure(validation.Errors);

        var config = await EnsureConfigurationAsync(userId);

        var server = new AgentMcpServer
        {
            AgentConfigurationId = config.Id,
            Name = dto.Name,
            Type = dto.Type,
            Url = dto.Url,
            EncryptedAuthHeader = _encryptor.Encrypt(dto.AuthHeader),
            Command = dto.Command,
            Args = dto.Args,
            EncryptedEnv = _encryptor.Encrypt(dto.Env),
            DisplayOrder = dto.DisplayOrder
        };

        _context.AgentMcpServers.Add(server);
        config.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult<int>.Success(server.Id);
    }

    public async Task<ServiceResult> UpdateMcpServerAsync(int mcpServerId, SaveAgentMcpServerDto dto)
    {
        var server = await _context.AgentMcpServers
            .Include(ms => ms.AgentConfiguration)
            .FirstOrDefaultAsync(ms => ms.Id == mcpServerId);

        if (server == null)
            return ServiceResult.Failure("MCP server not found.");

        server.Name = dto.Name;
        server.Type = dto.Type;
        server.Url = dto.Url;
        server.EncryptedAuthHeader = _encryptor.Encrypt(dto.AuthHeader);
        server.Command = dto.Command;
        server.Args = dto.Args;
        server.EncryptedEnv = _encryptor.Encrypt(dto.Env);
        server.DisplayOrder = dto.DisplayOrder;
        server.AgentConfiguration.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RemoveMcpServerAsync(int mcpServerId)
    {
        var server = await _context.AgentMcpServers
            .Include(ms => ms.AgentConfiguration)
            .FirstOrDefaultAsync(ms => ms.Id == mcpServerId);

        if (server == null)
            return ServiceResult.Failure("MCP server not found.");

        server.AgentConfiguration.UpdatedAt = DateTime.UtcNow;
        _context.AgentMcpServers.Remove(server);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    private async Task<ServiceResult> ValidateAgentUserAsync(string userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.Failure("User not found.");

        if (!user.IsAgent)
            return ServiceResult.Failure("User is not an agent.");

        return ServiceResult.Success();
    }

    private async Task<AgentConfiguration> EnsureConfigurationAsync(string userId)
    {
        var config = await _context.AgentConfigurations
            .FirstOrDefaultAsync(ac => ac.UserId == userId);

        if (config == null)
        {
            config = new AgentConfiguration
            {
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.AgentConfigurations.Add(config);
            await _context.SaveChangesAsync();
        }

        return config;
    }

    private async Task<AgentConfiguration?> LoadConfigurationAsync(string userId)
    {
        return await _context.AgentConfigurations
            .Include(ac => ac.Repositories.OrderBy(r => r.DisplayOrder))
            .Include(ac => ac.McpServers.OrderBy(ms => ms.DisplayOrder))
            .FirstOrDefaultAsync(ac => ac.UserId == userId);
    }

    private AgentConfigurationDto MapToDto(AgentConfiguration config, bool masked)
    {
        return new AgentConfigurationDto
        {
            Id = config.Id,
            UserId = config.UserId,
            PollingIntervalSeconds = config.PollingIntervalSeconds,
            Model = config.Model,
            AutoApproveTools = config.AutoApproveTools,
            DryRun = config.DryRun,
            TaskTimeoutSeconds = config.TaskTimeoutSeconds,
            SystemPrompt = config.SystemPrompt,
            RepositoryUrl = config.RepositoryUrl,
            RepositoryBranch = config.RepositoryBranch,
            RepositoryAccessToken = masked
                ? _encryptor.MaskForDisplay(_encryptor.Decrypt(config.EncryptedRepositoryAccessToken))
                : _encryptor.Decrypt(config.EncryptedRepositoryAccessToken),
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt,
            Repositories = config.Repositories.Select(r => new AgentRepositoryDto
            {
                Id = r.Id,
                ProjectName = r.ProjectName,
                Url = r.Url,
                Branch = r.Branch,
                AccessToken = masked
                    ? _encryptor.MaskForDisplay(_encryptor.Decrypt(r.EncryptedAccessToken))
                    : _encryptor.Decrypt(r.EncryptedAccessToken),
                DisplayOrder = r.DisplayOrder
            }).ToList(),
            McpServers = config.McpServers.Select(ms => new AgentMcpServerDto
            {
                Id = ms.Id,
                Name = ms.Name,
                Type = ms.Type,
                Url = ms.Url,
                AuthHeader = masked
                    ? _encryptor.MaskForDisplay(_encryptor.Decrypt(ms.EncryptedAuthHeader))
                    : _encryptor.Decrypt(ms.EncryptedAuthHeader),
                Command = ms.Command,
                Args = ms.Args,
                Env = masked
                    ? _encryptor.MaskForDisplay(_encryptor.Decrypt(ms.EncryptedEnv))
                    : _encryptor.Decrypt(ms.EncryptedEnv),
                DisplayOrder = ms.DisplayOrder
            }).ToList()
        };
    }
}
