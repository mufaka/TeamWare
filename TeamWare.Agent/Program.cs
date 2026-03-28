using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamWare.Agent;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        config.AddEnvironmentVariables(prefix: "TEAMWARE_AGENT_");
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<List<AgentIdentityOptions>>(
            context.Configuration.GetSection("Agents"));
        services.AddSingleton<ITeamWareMcpClientFactory, TeamWareMcpClientFactory>();
        services.AddSingleton<ICopilotClientWrapperFactory, CopilotClientWrapperFactory>();
        services.AddHostedService<AgentHostedService>();
    })
    .Build();

await host.RunAsync();
