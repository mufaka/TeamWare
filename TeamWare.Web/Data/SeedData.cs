using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Models;

namespace TeamWare.Web.Data;

public static class SeedData
{
    public const string AdminEmail = "admin@teamware.local";
    public const string AdminPassword = "Admin123!";
    public const string AdminDisplayName = "Administrator";
    public const string AdminRoleName = "Admin";
    public const string UserRoleName = "User";

    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();

        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(AdminRoleName))
        {
            await roleManager.CreateAsync(new IdentityRole(AdminRoleName));
        }

        if (!await roleManager.RoleExistsAsync(UserRoleName))
        {
            await roleManager.CreateAsync(new IdentityRole(UserRoleName));
        }

        var adminUser = await userManager.FindByEmailAsync(AdminEmail);

        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = AdminEmail,
                Email = AdminEmail,
                DisplayName = AdminDisplayName,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(adminUser, AdminPassword);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, AdminRoleName);
                await userManager.AddToRoleAsync(adminUser, UserRoleName);
            }
        }
        else
        {
            if (!await userManager.IsInRoleAsync(adminUser, UserRoleName))
            {
                await userManager.AddToRoleAsync(adminUser, UserRoleName);
            }
        }

        await SeedGlobalConfigurationAsync(context);
    }

    private static async Task SeedGlobalConfigurationAsync(ApplicationDbContext context)
    {
        if (!await context.GlobalConfigurations.AnyAsync(gc => gc.Key == "ATTACHMENT_DIR"))
        {
            context.GlobalConfigurations.Add(new GlobalConfiguration
            {
                Key = "ATTACHMENT_DIR",
                Value = "./Attachments",
                Description = "Base directory for file attachment storage",
                UpdatedAt = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
        }
    }
}
