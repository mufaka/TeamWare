using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using TeamWare.Web.Authentication;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Infrastructure;

public class PatAuthenticationHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly PersonalAccessTokenService _tokenService;
    private readonly ApplicationUser _user;
    private readonly UserManager<ApplicationUser> _userManager;

    public PatAuthenticationHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _user = new ApplicationUser
        {
            UserName = "testuser@test.com",
            Email = "testuser@test.com",
            DisplayName = "Test User",
            NormalizedUserName = "TESTUSER@TEST.COM",
            NormalizedEmail = "TESTUSER@TEST.COM"
        };

        _context.Users.Add(_user);
        _context.SaveChanges();

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
    public async Task HandleAuthenticateAsync_NoAuthHeader_ReturnsNoResult()
    {
        var (handler, _) = await CreateHandlerAsync(null);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_NonBearerHeader_ReturnsNoResult()
    {
        var (handler, _) = await CreateHandlerAsync("Basic dXNlcjpwYXNz");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_EmptyBearerToken_ReturnsNoResult()
    {
        var (handler, _) = await CreateHandlerAsync("Bearer ");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_InvalidToken_ReturnsFail()
    {
        var (handler, _) = await CreateHandlerAsync("Bearer tw_invalid_token_here");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ValidToken_ReturnsSuccess()
    {
        var createResult = await _tokenService.CreateTokenAsync(_user.Id, "Test Token", null);
        var rawToken = createResult.Data!;

        var (handler, _) = await CreateHandlerAsync($"Bearer {rawToken}");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal);
        var nameIdentifier = result.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        Assert.NotNull(nameIdentifier);
        Assert.Equal(_user.Id, nameIdentifier.Value);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ValidToken_IncludesNameClaim()
    {
        var createResult = await _tokenService.CreateTokenAsync(_user.Id, "Test Token", null);
        var rawToken = createResult.Data!;

        var (handler, _) = await CreateHandlerAsync($"Bearer {rawToken}");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        var nameClaim = result.Principal!.FindFirst(System.Security.Claims.ClaimTypes.Name);
        Assert.NotNull(nameClaim);
        Assert.Equal("testuser@test.com", nameClaim.Value);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ValidToken_IncludesEmailClaim()
    {
        var createResult = await _tokenService.CreateTokenAsync(_user.Id, "Test Token", null);
        var rawToken = createResult.Data!;

        var (handler, _) = await CreateHandlerAsync($"Bearer {rawToken}");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        var emailClaim = result.Principal!.FindFirst(System.Security.Claims.ClaimTypes.Email);
        Assert.NotNull(emailClaim);
        Assert.Equal("testuser@test.com", emailClaim.Value);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ExpiredToken_ReturnsFail()
    {
        var createResult = await _tokenService.CreateTokenAsync(
            _user.Id, "Expired Token", DateTime.UtcNow.AddSeconds(-1));
        var rawToken = createResult.Data!;

        var (handler, _) = await CreateHandlerAsync($"Bearer {rawToken}");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_RevokedToken_ReturnsFail()
    {
        var createResult = await _tokenService.CreateTokenAsync(_user.Id, "Revoked Token", null);
        var rawToken = createResult.Data!;

        var token = await _context.PersonalAccessTokens.FirstAsync();
        await _tokenService.RevokeTokenAsync(token.Id, _user.Id, isAdmin: false);

        var (handler, _) = await CreateHandlerAsync($"Bearer {rawToken}");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
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
