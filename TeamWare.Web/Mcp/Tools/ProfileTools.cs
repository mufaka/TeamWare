using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using ModelContextProtocol.Server;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Web.Mcp.Tools;

[McpServerToolType]
[Authorize(AuthenticationSchemes = TeamWare.Web.Authentication.PatAuthenticationHandler.SchemeName)]
public class ProfileTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool, Description("Get the authenticated user's profile information including agent status.")]
    public static async Task<string> get_my_profile(
        ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager,
        IAgentConfigurationService agentConfigurationService)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var appUser = await userManager.FindByIdAsync(userId);
        if (appUser == null)
        {
            return JsonSerializer.Serialize(new { error = "User not found." }, JsonOptions);
        }

        object? configuration = null;

        if (appUser.IsAgent)
        {
            var configResult = await agentConfigurationService.GetDecryptedConfigurationAsync(userId);
            if (configResult.Succeeded && configResult.Data != null)
            {
                var config = configResult.Data;
                configuration = BuildConfigurationObject(config);
            }
        }

        var profile = new
        {
            userId = appUser.Id,
            displayName = appUser.DisplayName,
            email = appUser.Email,
            isAgent = appUser.IsAgent,
            agentDescription = appUser.IsAgent ? appUser.AgentDescription : (string?)null,
            isAgentActive = appUser.IsAgent ? appUser.IsAgentActive : (bool?)null,
            lastActiveAt = appUser.LastActiveAt?.ToString("O"),
            configuration
        };

        return JsonSerializer.Serialize(profile, JsonOptions);
    }

    private static JsonElement? ParseJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object BuildConfigurationObject(ViewModels.AgentConfigurationDto config)
    {
        var repositories = config.Repositories.Count > 0
            ? config.Repositories.Select(r => new
            {
                projectName = r.ProjectName,
                url = r.Url,
                branch = r.Branch,
                accessToken = r.AccessToken
            }).ToArray()
            : null;

        var mcpServers = config.McpServers.Count > 0
            ? config.McpServers.Select(s => new
            {
                name = s.Name,
                type = s.Type,
                url = s.Url,
                authHeader = s.AuthHeader,
                command = s.Command,
                args = ParseJsonOrNull(s.Args),
                env = ParseJsonOrNull(s.Env)
            }).ToArray()
            : null;

        return new
        {
            pollingIntervalSeconds = config.PollingIntervalSeconds,
            model = config.Model,
            autoApproveTools = config.AutoApproveTools,
            dryRun = config.DryRun,
            taskTimeoutSeconds = config.TaskTimeoutSeconds,
            systemPrompt = config.SystemPrompt,
            repositoryUrl = config.RepositoryUrl,
            repositoryBranch = config.RepositoryBranch,
            repositoryAccessToken = config.RepositoryAccessToken,
            repositories,
            mcpServers,
            agentBackend = config.AgentBackend,
            codexApiKey = config.CodexApiKey
        };
    }
}
