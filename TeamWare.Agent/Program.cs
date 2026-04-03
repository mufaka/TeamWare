using GitHub.Copilot.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamWare.Agent;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

if (args.Contains("--list-models", StringComparer.OrdinalIgnoreCase))
{
    await using var client = new CopilotClient(new CopilotClientOptions());
    await client.StartAsync();

    var models = await client.ListModelsAsync();

    Console.WriteLine($"{"Model ID",-45} {"Name",-35} {"Context Window"}");
    Console.WriteLine(new string('-', 100));

    foreach (var model in models.OrderBy(m => m.Id))
    {
        var contextWindow = model.Capabilities?.Limits?.MaxContextWindowTokens;
        var contextStr = contextWindow.HasValue ? contextWindow.Value.ToString("N0") : "N/A";
        Console.WriteLine($"{model.Id,-45} {model.Name,-35} {contextStr}");
    }

    return;
}

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
