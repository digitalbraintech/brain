namespace DigitalBrain.Abstractions.Scripting;

public sealed partial class ApplicationDefinition
{
    internal Task<ApplicationInvocation> ReadInvocationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        using var actor = EnterActor();
        return _kernel.ReadInvocation(operationId).WaitAsync(cancellationToken);
    }

    internal async Task<ApplicationInvocation> CancelInvocationAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        using var actor = EnterActor();
        await _kernel.Cancel(operationId).WaitAsync(cancellationToken).ConfigureAwait(false);
        return await _kernel.ReadInvocation(operationId).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ApplicationInvocation> InvokeDeclaredAsync(string operation, string inputJson,
        Guid operationId, CancellationToken cancellationToken)
    {
        ApplicationRunScope.ThrowIfDefinitionEffect("Invoke application");
        using var actor = EnterActor();
        await _kernel.SubmitDeclared(operationId, operation, inputJson).WaitAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var result = await _kernel.ReadInvocation(operationId).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result.Status is "completed" or "failed" or "cancelled")
            {
                return result;
            }
            if (result.Status is not ("pending" or "waiting" or "running"))
            {
                throw new InvalidOperationException($"Application invocation returned unknown status '{result.Status}'.");
            }
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }
}
