using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Controllers;

public class AccountControllerTests : IClassFixture<TeamWareWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TeamWareWebApplicationFactory _factory;

    public AccountControllerTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Register_Get_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/Account/Register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Register", content);
    }

    [Fact]
    public async Task Login_Get_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Log in", content);
    }

    [Fact]
    public async Task Register_Post_WithValidData_RedirectsToHome()
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync("/Account/Register");

        var formData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", antiForgeryToken },
            { "DisplayName", "Test User" },
            { "Email", "newuser@example.com" },
            { "Password", "TestPass1" },
            { "ConfirmPassword", "TestPass1" }
        };

        var response = await _client.PostAsync("/Account/Register", new FormUrlEncodedContent(formData));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_Post_WithValidCredentials_RedirectsToHome()
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync("/Account/Login");

        var formData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", antiForgeryToken },
            { "Email", SeedData.AdminEmail },
            { "Password", SeedData.AdminPassword },
            { "RememberMe", "false" }
        };

        var response = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(formData));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_Post_WithInvalidCredentials_ReturnsLoginPage()
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync("/Account/Login");

        var formData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", antiForgeryToken },
            { "Email", "bad@example.com" },
            { "Password", "WrongPass1" },
            { "RememberMe", "false" }
        };

        var response = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(formData));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt", content);
    }

    [Fact]
    public async Task Logout_Post_RedirectsToHome()
    {
        // First login
        var loginToken = await GetAntiForgeryTokenAsync("/Account/Login");
        var loginData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", loginToken },
            { "Email", SeedData.AdminEmail },
            { "Password", SeedData.AdminPassword },
            { "RememberMe", "false" }
        };

        var loginResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(loginData));
        var cookies = loginResponse.Headers.GetValues("Set-Cookie");

        // Then logout
        var logoutToken = await GetAntiForgeryTokenAsync("/Account/Login");
        var logoutData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logoutToken }
        };

        var response = await _client.PostAsync("/Account/Logout", new FormUrlEncodedContent(logoutData));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ResetPassword_Get_WithoutAuth_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/Account/ResetPassword");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? "";
        Assert.Contains("/Account/Login", location);
    }

    [Fact]
    public async Task AdminSeed_CreatesAdminUser()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);

        Assert.NotNull(admin);
        Assert.Equal(SeedData.AdminDisplayName, admin.DisplayName);
        Assert.True(await userManager.IsInRoleAsync(admin, SeedData.AdminRoleName));
    }

    [Fact]
    public async Task Register_Post_WithValidData_AssignsUserRole()
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync("/Account/Register");
        var uniqueEmail = $"roletest-{Guid.NewGuid():N}@example.com";

        var formData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", antiForgeryToken },
            { "DisplayName", "Role Test User" },
            { "Email", uniqueEmail },
            { "Password", "TestPass1" },
            { "ConfirmPassword", "TestPass1" }
        };

        await _client.PostAsync("/Account/Register", new FormUrlEncodedContent(formData));

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(uniqueEmail);

        Assert.NotNull(user);
        Assert.True(await userManager.IsInRoleAsync(user, SeedData.UserRoleName));
    }

    private async Task<string> GetAntiForgeryTokenAsync(string url)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Extract the anti-forgery token from the form
        var tokenStart = content.IndexOf("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"");
        if (tokenStart == -1)
        {
            tokenStart = content.IndexOf("value=\"", content.IndexOf("__RequestVerificationToken"));
            if (tokenStart == -1)
                return string.Empty;
            tokenStart = content.IndexOf("value=\"", tokenStart) + 7;
        }
        else
        {
            tokenStart += "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"".Length;
        }

        var tokenEnd = content.IndexOf("\"", tokenStart);
        return content[tokenStart..tokenEnd];
    }
}
