using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Helpers;

/// <summary>
/// Factory for creating a UserManager backed by an ApplicationDbContext
/// for use in unit tests that need user lookup.
/// </summary>
public static class TestUserManagerFactory
{
    public static UserManager<ApplicationUser> Create(ApplicationDbContext context)
    {
        var store = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(context);
        var options = Options.Create(new IdentityOptions());
        var hasher = new PasswordHasher<ApplicationUser>();

        return new UserManager<ApplicationUser>(
            store,
            options,
            hasher,
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }
}
