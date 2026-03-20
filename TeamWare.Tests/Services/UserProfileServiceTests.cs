using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class UserProfileServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserProfileService _profileService;

    public UserProfileServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(_connection));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated();

        _userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _profileService = new UserProfileService(_userManager);
    }

    private async Task<ApplicationUser> CreateUser(
        string email = "test@test.com",
        string displayName = "Test User",
        string password = "TestPass1!")
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };
        await _userManager.CreateAsync(user, password);
        return user;
    }

    // --- GetProfile ---

    [Fact]
    public async Task GetProfile_EmptyUserId_ReturnsFailure()
    {
        var result = await _profileService.GetProfile("");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    [Fact]
    public async Task GetProfile_NonExistentUser_ReturnsFailure()
    {
        var result = await _profileService.GetProfile("nonexistent-id");

        Assert.False(result.Succeeded);
        Assert.Contains("User not found.", result.Errors);
    }

    [Fact]
    public async Task GetProfile_ValidUser_ReturnsUserData()
    {
        var user = await CreateUser();

        var result = await _profileService.GetProfile(user.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Test User", result.Data!.DisplayName);
        Assert.Equal("test@test.com", result.Data.Email);
    }

    // --- UpdateProfile ---

    [Fact]
    public async Task UpdateProfile_EmptyUserId_ReturnsFailure()
    {
        var result = await _profileService.UpdateProfile("", "Name", null);

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    [Fact]
    public async Task UpdateProfile_EmptyDisplayName_ReturnsFailure()
    {
        var user = await CreateUser();
        var result = await _profileService.UpdateProfile(user.Id, "", null);

        Assert.False(result.Succeeded);
        Assert.Contains("Display name is required.", result.Errors);
    }

    [Fact]
    public async Task UpdateProfile_DisplayNameTooLong_ReturnsFailure()
    {
        var user = await CreateUser();
        var result = await _profileService.UpdateProfile(user.Id, new string('a', 101), null);

        Assert.False(result.Succeeded);
        Assert.Contains("Display name must not exceed 100 characters.", result.Errors);
    }

    [Fact]
    public async Task UpdateProfile_AvatarUrlTooLong_ReturnsFailure()
    {
        var user = await CreateUser();
        var result = await _profileService.UpdateProfile(user.Id, "Name", new string('a', 501));

        Assert.False(result.Succeeded);
        Assert.Contains("Avatar URL must not exceed 500 characters.", result.Errors);
    }

    [Fact]
    public async Task UpdateProfile_NonExistentUser_ReturnsFailure()
    {
        var result = await _profileService.UpdateProfile("nonexistent", "Name", null);

        Assert.False(result.Succeeded);
        Assert.Contains("User not found.", result.Errors);
    }

    [Fact]
    public async Task UpdateProfile_ValidData_UpdatesUser()
    {
        var user = await CreateUser();

        var result = await _profileService.UpdateProfile(user.Id, "New Name", "https://example.com/avatar.png");

        Assert.True(result.Succeeded);

        var updated = await _userManager.FindByIdAsync(user.Id);
        Assert.Equal("New Name", updated!.DisplayName);
        Assert.Equal("https://example.com/avatar.png", updated.AvatarUrl);
    }

    [Fact]
    public async Task UpdateProfile_NullAvatarUrl_ClearsAvatar()
    {
        var user = await CreateUser();
        user.AvatarUrl = "https://example.com/old.png";
        await _userManager.UpdateAsync(user);

        var result = await _profileService.UpdateProfile(user.Id, "Name", null);

        Assert.True(result.Succeeded);
        var updated = await _userManager.FindByIdAsync(user.Id);
        Assert.Null(updated!.AvatarUrl);
    }

    // --- ChangePassword ---

    [Fact]
    public async Task ChangePassword_EmptyUserId_ReturnsFailure()
    {
        var result = await _profileService.ChangePassword("", "old", "new");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    [Fact]
    public async Task ChangePassword_EmptyCurrentPassword_ReturnsFailure()
    {
        var user = await CreateUser();
        var result = await _profileService.ChangePassword(user.Id, "", "NewPass1!");

        Assert.False(result.Succeeded);
        Assert.Contains("Current password is required.", result.Errors);
    }

    [Fact]
    public async Task ChangePassword_EmptyNewPassword_ReturnsFailure()
    {
        var user = await CreateUser();
        var result = await _profileService.ChangePassword(user.Id, "TestPass1!", "");

        Assert.False(result.Succeeded);
        Assert.Contains("New password is required.", result.Errors);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ReturnsFailure()
    {
        var user = await CreateUser();

        var result = await _profileService.ChangePassword(user.Id, "WrongPass1!", "NewPass1!");

        Assert.False(result.Succeeded);
        Assert.True(result.Errors.Count > 0);
    }

    [Fact]
    public async Task ChangePassword_ValidData_ChangesPassword()
    {
        var user = await CreateUser();

        var result = await _profileService.ChangePassword(user.Id, "TestPass1!", "NewPass1!");

        Assert.True(result.Succeeded);

        // Verify new password works
        var checkResult = await _userManager.CheckPasswordAsync(
            (await _userManager.FindByIdAsync(user.Id))!, "NewPass1!");
        Assert.True(checkResult);
    }

    [Fact]
    public async Task ChangePassword_WeakNewPassword_ReturnsFailure()
    {
        var user = await CreateUser();

        var result = await _profileService.ChangePassword(user.Id, "TestPass1!", "weak");

        Assert.False(result.Succeeded);
        Assert.True(result.Errors.Count > 0);
    }

    // --- UpdateThemePreference ---

    [Fact]
    public async Task UpdateThemePreference_EmptyUserId_ReturnsFailure()
    {
        var result = await _profileService.UpdateThemePreference("", "dark");

        Assert.False(result.Succeeded);
        Assert.Contains("User ID is required.", result.Errors);
    }

    [Fact]
    public async Task UpdateThemePreference_InvalidTheme_ReturnsFailure()
    {
        var user = await CreateUser();
        var result = await _profileService.UpdateThemePreference(user.Id, "neon");

        Assert.False(result.Succeeded);
        Assert.Contains("Theme must be 'light', 'dark', or 'system'.", result.Errors);
    }

    [Fact]
    public async Task UpdateThemePreference_NonExistentUser_ReturnsFailure()
    {
        var result = await _profileService.UpdateThemePreference("nonexistent", "dark");

        Assert.False(result.Succeeded);
        Assert.Contains("User not found.", result.Errors);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("system")]
    public async Task UpdateThemePreference_ValidThemes_UpdatesUser(string theme)
    {
        var user = await CreateUser($"{theme}@test.com", $"User {theme}");

        var result = await _profileService.UpdateThemePreference(user.Id, theme);

        Assert.True(result.Succeeded);
        var updated = await _userManager.FindByIdAsync(user.Id);
        Assert.Equal(theme, updated!.ThemePreference);
    }

    [Fact]
    public async Task UpdateThemePreference_CaseInsensitive()
    {
        var user = await CreateUser();

        var result = await _profileService.UpdateThemePreference(user.Id, "DARK");

        Assert.True(result.Succeeded);
        var updated = await _userManager.FindByIdAsync(user.Id);
        Assert.Equal("dark", updated!.ThemePreference);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
