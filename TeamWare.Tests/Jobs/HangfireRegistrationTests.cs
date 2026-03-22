using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace TeamWare.Tests.Jobs;

public class HangfireRegistrationTests : IClassFixture<TeamWareWebApplicationFactory>
{
    private readonly TeamWareWebApplicationFactory _factory;

    public HangfireRegistrationTests(TeamWareWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void HangfireServices_AreRegistered()
    {
        // Arrange: create the app to trigger service registration
        using var client = _factory.CreateClient();

        // Act: resolve Hangfire services
        using var scope = _factory.Services.CreateScope();
        var jobStorage = scope.ServiceProvider.GetService<JobStorage>();
        var recurringJobManager = scope.ServiceProvider.GetService<IRecurringJobManager>();

        // Assert
        Assert.NotNull(jobStorage);
        Assert.NotNull(recurringJobManager);
    }

    [Fact]
    public void LoungeRetentionJob_IsRegisteredAsRecurringJob()
    {
        // Arrange: create the app to trigger job registration
        using var client = _factory.CreateClient();

        // Act: query Hangfire storage for recurring jobs
        using var scope = _factory.Services.CreateScope();
        var jobStorage = scope.ServiceProvider.GetRequiredService<JobStorage>();
        using var connection = jobStorage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        // Assert: the lounge retention job should be registered with daily schedule
        var retentionJob = recurringJobs.FirstOrDefault(j => j.Id == "lounge-retention-cleanup");
        Assert.NotNull(retentionJob);
        Assert.Equal(Cron.Daily(), retentionJob.Cron);
    }
}
