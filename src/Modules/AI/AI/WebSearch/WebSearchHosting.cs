using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI.WebSearch;

internal static class WebSearchHosting
{
    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>(TavilyWebSearch.EnabledConfigurationKey))
        {
            return;
        }

        services.AddHttpClient<IWebSearch, TavilyWebSearch>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolSource, WebSearchToolSource>());
    }
}
