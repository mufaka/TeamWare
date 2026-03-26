using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using ModelContextProtocol.Server;
using TeamWare.Web.Models;

namespace TeamWare.Web.Mcp.Tools;

[McpServerToolType]
[Authorize(AuthenticationSchemes = TeamWare.Web.Authentication.PatAuthenticationHandler.SchemeName)]
public class ProfileTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool, Description("Get the authenticated user's profile information including agent status.")]
    public static async Task<string> get_my_profile(
        ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var appUser = await userManager.FindByIdAsync(userId);
        if (appUser == null)
        {
            return JsonSerializer.Serialize(new { error = "User not found." }, JsonOptions);
        }

        var profile = new
        {
            userId = appUser.Id,
            displayName = appUser.DisplayName,
            email = appUser.Email,
            isAgent = appUser.IsAgent,
            agentDescription = appUser.IsAgent ? appUser.AgentDescription : (string?)null,
            isAgentActive = appUser.IsAgent ? appUser.IsAgentActive : (bool?)null,
            lastActiveAt = appUser.LastActiveAt?.ToString("O")
        };

        return JsonSerializer.Serialize(profile, JsonOptions);
    }
}
