using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Web.Authentication;

public class PatAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PersonalAccessToken";

    private readonly IPersonalAccessTokenService _tokenService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;

    public PatAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IPersonalAccessTokenService tokenService,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
        _userManager = userManager;
        _dbContext = dbContext;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorizationHeader = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var rawToken = authorizationHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrEmpty(rawToken))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await _tokenService.ValidateTokenAsync(rawToken);

        if (!result.Succeeded)
        {
            return AuthenticateResult.Fail(result.Errors.FirstOrDefault() ?? "Invalid token.");
        }

        var user = result.Data!;

        if (user.IsAgent && !user.IsAgentActive)
        {
            return AuthenticateResult.Fail("Agent is currently paused.");
        }

        // Update LastActiveAt for agent users on every MCP request
        if (user.IsAgent)
        {
            user.LastActiveAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty)
        };

        if (user.IsAgent)
        {
            claims.Add(new Claim("IsAgent", "true"));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
