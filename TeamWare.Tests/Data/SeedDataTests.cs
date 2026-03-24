using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Data;

public class SeedDataTests : IClassFixture<TeamWareWebApplicationFactory>
{
    private readonly TeamWareWebApplicationFactory _factory;

    public SeedDataTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_CreatesAdminRole()
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        Assert.True(await roleManager.RoleExistsAsync(SeedData.AdminRoleName));
    }

    [Fact]
    public async Task Seed_CreatesUserRole()
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        Assert.True(await roleManager.RoleExistsAsync(SeedData.UserRoleName));
    }

    [Fact]
    public async Task Seed_AdminUserHasAdminRole()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);

        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin, SeedData.AdminRoleName));
    }

    [Fact]
    public async Task Seed_AdminUserHasUserRole()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync(SeedData.AdminEmail);

        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin, SeedData.UserRoleName));
    }

    [Fact]
    public async Task Seed_CreatesAttachmentDirConfiguration()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var config = await context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == "ATTACHMENT_DIR");

        Assert.NotNull(config);
        Assert.Equal("./Attachments", config.Value);
        Assert.Equal("Base directory for file attachment storage", config.Description);
    }

    [Fact]
    public async Task Seed_AttachmentDirIsNotDuplicated()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var count = await context.GlobalConfigurations
            .CountAsync(gc => gc.Key == "ATTACHMENT_DIR");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Seed_CreatesOllamaUrlConfiguration()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var config = await context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == "OLLAMA_URL");

        Assert.NotNull(config);
        Assert.Equal("", config.Value);
        Assert.Equal("Base URL of the Ollama instance (e.g., http://ollama-host:11434). Leave empty to disable AI features.", config.Description);
    }

    [Fact]
    public async Task Seed_CreatesOllamaModelConfiguration()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var config = await context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == "OLLAMA_MODEL");

        Assert.NotNull(config);
        Assert.Equal("", config.Value);
        Assert.Equal("Ollama model name for AI completions. Defaults to llama3.1 when empty.", config.Description);
    }

    [Fact]
    public async Task Seed_CreatesOllamaTimeoutConfiguration()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var config = await context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == "OLLAMA_TIMEOUT");

        Assert.NotNull(config);
        Assert.Equal("", config.Value);
        Assert.Equal("AI request timeout in seconds. Defaults to 60 when empty.", config.Description);
    }

    [Fact]
    public async Task Seed_CreatesMcpEnabledConfiguration()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var config = await context.GlobalConfigurations
            .FirstOrDefaultAsync(gc => gc.Key == "MCP_ENABLED");

        Assert.NotNull(config);
        Assert.Equal("false", config.Value);
        Assert.Equal("Enable the MCP (Model Context Protocol) server endpoint at /mcp. Set to true to allow AI agents and MCP clients to connect.", config.Description);
    }
}
