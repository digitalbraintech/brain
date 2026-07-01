using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

[GrainType("db.support.v1")]
public class DbSupportNeuron : Neuron, IDbSupportNeuron
{
    public DbSupportNeuron(ILogger<DbSupportNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(DbConnect cmd)
    {
        Logger.LogInformation("DB connected {Name} via {Provider}", cmd.ConnectionName, cmd.Provider);
        await FireAsync(cmd);
    }

    public async Task HandleAsync(DbQuery cmd)
    {
        Logger.LogInformation("DB query on {Name}: {Q}", cmd.ConnectionName, cmd.Query);
        var result = $"[DB result for {cmd.Query}] 42 rows";
        await FireAsync(new DbQuery(cmd.ConnectionName, cmd.Query, result));
    }
}
