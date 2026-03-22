using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamWare.Web.Data;

namespace TeamWare.Tests;

public class TeamWareWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public TeamWareWebApplicationFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add an in-memory SQLite database for testing using the shared connection
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Replace Hangfire storage with a fresh per-factory MemoryStorage instance
            // to avoid distributed lock contention between parallel test factory instances
            var storageDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(JobStorage));

            if (storageDescriptor != null)
            {
                services.Remove(storageDescriptor);
            }

            var freshStorage = new MemoryStorage();
            services.AddSingleton<JobStorage>(freshStorage);

            // Remove Hangfire background job server to prevent interference during tests
            var hangfireHostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType != null
                    && d.ImplementationType.FullName != null
                    && d.ImplementationType.FullName.Contains("Hangfire"))
                .ToList();

            foreach (var svc in hangfireHostedServices)
            {
                services.Remove(svc);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
