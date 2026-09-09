using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.GitHub;
// Non-secret metadata only. Operator App private keys and transient browser tokens never enter storage.
[GenerateSerializer, Alias("github.connection-record")]
internal sealed record GitHubConnectionRecord([property: Id(0)] string Id, [property: Id(1)] OwnerId Owner, [property: Id(2)] PrincipalId Principal, [property: Id(3)] long AppId, [property: Id(4)] long InstallationId, [property: Id(5)] long RepositoryId, [property: Id(6)] string RepositoryOwner, [property: Id(7)] string RepositoryName, [property: Id(8)] string Epoch);
[GenerateSerializer, Alias("github.connections-state")]
internal sealed record GitHubConnectionsState([property: Id(0)] List<GitHubConnectionRecord> Connections);
[Alias("github.connection-registry")]
internal interface IGitHubConnectionRegistry : IGrainWithStringKey
{
    Task<GitHubConnectionRecord[]> ReadAsync();
    Task StoreAsync(GitHubConnectionRecord connection);
}

[GrainType("github-connection-registry")]
internal sealed class GitHubConnectionRegistry([PersistentState("connections", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GitHubConnectionsState> state) : Grain, IGitHubConnectionRegistry
{
    public Task<GitHubConnectionRecord[]> ReadAsync() => Task.FromResult(state.State?.Connections?.ToArray() ?? []);
    public async Task StoreAsync(GitHubConnectionRecord connection)
    {
        if (VerifiedActor.Current?.PrincipalId != connection.Principal)
        {
            throw new UnauthorizedAccessException("A GitHub connection must belong to the authenticated principal.");
        }

        var previous = state.State;
        var items = previous?.Connections?.Where(item => item.Id != connection.Id).ToList() ?? [];
        if (items.Count >= 256)
        {
            throw new InvalidOperationException("The GitHub connection capacity is full.");
        }

        items.Add(connection);
        state.State = new(items);
        try
        {
            await state.WriteStateAsync();
        }
        catch
        {
            state.State = previous!;
            throw;
        }
    }
}
