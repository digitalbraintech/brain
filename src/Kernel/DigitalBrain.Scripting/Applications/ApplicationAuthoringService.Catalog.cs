using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Scripting.Applications;

public sealed partial class ApplicationAuthoringService
{
    private static readonly JsonSerializerOptions CatalogJson = JsonSerializerOptions.Default;

    public Task<ApplicationCapabilityCatalog> CatalogAsync(
        IDigitalBrain brain,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = TenantDirectory(brain);
        var neurons = neuronCapabilities
            .OrderBy(static registration => registration.NeuronType, StringComparer.Ordinal)
            .Select(registration => new ApplicationNeuronCapability(
                registration.Key,
                registration.ContractType.FullName ?? registration.ContractType.Name,
                registration.DefaultInstanceName,
                ResolveProject(registration.ProjectPath),
                [.. neuronInputs
                    .Where(input => input.IsPublic &&
                                    StringComparer.Ordinal.Equals(input.NeuronType, registration.NeuronType))
                    .OrderBy(static input => input.InputKey, StringComparer.Ordinal)
                    .Select(input => new ApplicationNeuronInputCapability(
                        input.InputKey!, DescribeCatalogContract(input.SignalType, input.Contract),
                        input.ResponseType is null ? null : DescribeCatalogContract(input.ResponseType)))],
                [.. neuronEvents
                    .Where(output => output.IsPublic &&
                                     StringComparer.Ordinal.Equals(output.NeuronType, registration.NeuronType))
                    .OrderBy(static output => output.EventKey, StringComparer.Ordinal)
                    .Select(output => new ApplicationNeuronEventCapability(
                        output.EventKey, DescribeCatalogContract(output.SignalType, output.Contract))) ]))
            .ToArray();
        return Task.FromResult(new ApplicationCapabilityCatalog(neurons));
    }

    private static ApplicationJsonContract DescribeCatalogContract(Type type, string? registeredName = null)
    {
        var declared = type.GetCustomAttributes(typeof(ApplicationJsonContractAttribute), inherit: false)
            .OfType<ApplicationJsonContractAttribute>()
            .SingleOrDefault();
        var name = registeredName ?? (declared is null ? null : $"{declared.StableName}/v{declared.SchemaVersion}")
            ?? throw new InvalidOperationException(
                $"Public application contract '{type.FullName}' has no stable JSON contract name.");
        JsonNode schema = CatalogJson.GetJsonSchemaAsNode(type);
        return new(name, schema.ToJsonString(), null, type.FullName);
    }

    private static string ResolveProject(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate).Replace('\\', '/');
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            $"The installed application SDK project '{relativePath}' is unavailable on this host.");
    }
}
