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
            }
        }
    }
}
