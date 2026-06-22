namespace Aspire.Hosting.ApplicationModel;

public sealed class DigitalBrainResource(string name) : Resource(name), IResourceWithConnectionString
{
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"digitalbrain://{Name}");

    public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(ConnectionStringExpression.ValueExpression);
}