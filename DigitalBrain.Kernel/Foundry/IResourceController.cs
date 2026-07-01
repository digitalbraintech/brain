namespace DigitalBrain.Kernel.Foundry;

public interface IResourceController
{
    Task RestartSiloAsync(string reason);
}

public sealed class AspireResourceController(ILogger<AspireResourceController> logger) : IResourceController
{
    private readonly ILogger<AspireResourceController> _logger = logger;

    public Task RestartSiloAsync(string reason)
    {
        // The actual restart is performed out-of-band by the Aspire MCP tool
        // execute_resource_command("restart","silo"). This controller records intent;
        // the orchestrator emits SiloRestartRequested which the MCP-driven loop consumes.
        _logger.LogWarning("Silo restart requested: {Reason}. Trigger via Aspire MCP execute_resource_command(restart, silo).", reason);
        return Task.CompletedTask;
    }
}
