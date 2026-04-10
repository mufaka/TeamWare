using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using TeamWare.Web.Authentication;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Infrastructure;

public class PatAuthenticationHandlerAgentTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly PersonalAccessTokenService _tokenService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PatAuthenticationHandlerAgentTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _tokenService = new PersonalAccessTokenService(_context);

        var userStore = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(_context);
        _userManager = new UserManager<ApplicationUser>(
            userStore,
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            new NullLogger<UserManager<ApplicationUser>>());
    }

    private ApplicationUser CreateUser(bool isAgent, bool isAgentActive = true)
    {
        var user = new ApplicationUser
        {
            UserName = $"user-{Guid.NewGuid():N}@test.com",
            Email = $"user-{Guid.NewGuid():N}@test.com",
            DisplayName = isAgent ? "Test Agent" : "Test Human",
            NormalizedUserName = $"USER-{Guid.NewGuid():N}@TEST.COM",
            NormalizedEmail = $"USER-{Guid.NewGuid():N}@TEST.COM",
            IsAgent = isAgent,
            IsAgentActive = isAgentActive
        };

        _context.Users.Add(user);
        _context.SaveChanges();
        return user;
    }

    private async Task<(PatAuthenticationHandler handler, HttpContext httpContext)> CreateHandlerAsync(string? authorizationHeader)
    {
        var httpContext = new DefaultHttpContext();

        if (authorizationHeader != null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var optionsMonitor = new TestOptionsMonitor();
        var handler = new PatAuthenticationHandler(
            optionsMonitor,
            new NullLoggerFactory(),
            UrlEncoder.Default,
            _tokenService,
            _userManager,
            _context);

        await handler.InitializeAsync(
            new AuthenticationScheme(PatAuthenticationHandler.SchemeName, null, typeof(PatAuthenticationHandler)),
            httpContext);

        return (handler, httpContext);
    }

    [Fact]
    public async Task AgentUser_ActiveAgent_AuthenticatesSuccessfully()
    {
        var agent = CreateUser(isAgent: true, isAgentActive: true);
        var tokenResult = await _tokenService.CreateTokenAsync(agent.Id, "Agent Token", null);

        var (handler, _) = await CreateHandlerAsync($"Bearer {tokenResult.Data!}");
        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AgentUser_ActiveAgent_HasIsAgentClaim()
    {
        var agent = CreateUser(isAgent: true, isAgentActive: true);
        var tokenResult = await _tokenService.CreateTokenAsync(agent.Id, "Agent Token", null);

        var (handler, _) = await CreateHandlerAsync($"Bearer {tokenResult.Data!}");
        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        var isAgentClaim = result.Principal!.FindFirst("IsAgent");
        Assert.NotNull(isAgentClaim);
        Assert.Equal("true", isAgentClaim.Value);
    }

    [Fact]
    public async Task AgentUser_PausedAgent_FailsAuthentication()
    {
        var agent = CreateUser(isAgent: true, isAgentActive: false);
        var tokenResult = await _tokenService.CreateTokenAsync(agent.Id, "Agent Token", null);

        var (handler, _) = await CreateHandlerAsync($"Bearer {tokenResult.Data!}");
        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("paused", result.Failure!.Message);
    }

    [Fact]
    public async Task HumanUser_DoesNotHaveIsAgentClaim()
    {
        var human = CreateUser(isAgent: false);
        var tokenResult = await _tokenService.CreateTokenAsync(human.Id, "Human Token", null);

        var (handler, _) = await CreateHandlerAsync($"Bearer {tokenResult.Data!}");
        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        var isAgentClaim = result.Principal!.FindFirst("IsAgent");
        Assert.Null(isAgentClaim);
    }

    [Fact]
    public async Task HumanUser_IsAgentActiveIgnored()
    {
        var human = CreateUser(isAgent: false);
        human.IsAgentActive = false;
        _context.SaveChanges();

        var tokenResult = await _tokenService.CreateTokenAsync(human.Id, "Human Token", null);

        var (handler, _) = await CreateHandlerAsync($"Bearer {tokenResult.Data!}");
        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AgentUser_ActiveAgent_UpdatesLastActiveAt()
    {
        var agent = CreateUser(isAgent: true, isAgentActive: true);
        Assert.Null(agent.LastActiveAt);

        var tokenResult = await _tokenService.CreateTokenAsync(agent.Id, "Agent Token", null);

        var (handler, _) = await CreateHandlerAsync($"Bearer {tokenResult.Data!}");
        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);

        // Reload the user from the database to get the updated value
        var updatedAgent = await _context.Users.FindAsync(agent.Id);
        Assert.NotNull(updatedAgent!.LastActiveAt);
        Assert.True(updatedAgent.LastActiveAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task HumanUser_DoesNotUpdateLastActiveAt()
    {
        var human = CreateUser(isAgent: false);
        Assert.Null(human.LastActiveAt);

        var tokenResult = await _tokenService.CreateTokenAsync(human.Id, "Human Token", null);

        var (handler, _) = await CreateHandlerAsync($"Bearer {tokenResult.Data!}");
        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);

        // Reload the user from the database
        var updatedHuman = await _context.Users.FindAsync(human.Id);
        Assert.Null(updatedHuman!.LastActiveAt);
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    private class TestOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue => new();

        public AuthenticationSchemeOptions Get(string? name) => new();

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
