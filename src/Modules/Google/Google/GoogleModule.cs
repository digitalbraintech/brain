using DigitalBrain.Product.Interactions;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Google;

public sealed class GoogleModule : IModule
{
    public const string GmailOAuthConfigurationRoot = "DigitalBrain:Google:Gmail:OAuth";

    public static readonly Uri GmailMcpEndpoint = new("https://gmailmcp.googleapis.com/mcp/v1");

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddSingleton(new NeuronPresentation("gmail", "Gmail", "Google", "gmail"));
        var alias = builder.Configuration["DigitalBrain:Google:Gmail:Alias"] ?? "gmail-local";
        _ = PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), alias);
        services.AddSingleton<IAgentToolSource>(new AgentDelegation<IGmail>("ask_gmail",
            "Ask the Gmail specialist to find/read email, inspect the selected account, or prepare an exact draft preview. "
            + "The specialist owns Gmail tools and browser login. Draft creation requires fresh trusted user confirmation; email is never sent.", alias));
        if (DigitalBrainFakes.Enabled(builder.Configuration))
        {
            services.AddKeyedSingleton<IAgentToolSource, FakeGmailTools>("gmail");
            return;
        }

        var settings = new GmailOAuthConfiguration(builder.Configuration);
        services.AddSingleton(settings);
        services.AddSingleton<GmailConnections>();
        services.AddSingleton<GmailLogins>();
        services.AddSingleton<GmailMcp>();
        services.AddSingleton<GmailDraftPreviews>();
        services.AddSingleton<IUserActionSource>(static s => s.GetRequiredService<GmailLogins>());
        services.AddSingleton<IHttpSurface>(static s => new BrowserLoginSurface(s.GetRequiredService<GmailLogins>()));
        services.AddSingleton<ITrustedUserCommandHandler>(static s => s.GetRequiredService<GmailDraftPreviews>());
        services.AddKeyedSingleton<IAgentToolSource, GmailTools>("gmail");
        services.AddHostedService<BrowserLoginWorker<GmailLogins>>();
        services.AddHostedService<GmailMaintenanceWorker>();
        services.AddGmailAuthentication(settings, GmailLogins.LoginDefinition);
    }
}
