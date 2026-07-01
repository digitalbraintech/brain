using DigitalBrain.Core;
using Orleans;

namespace DigitalBrain.TestKit;

public interface IDigitalBrain
{
    Task FireAsync<T>(T synapse) where T : Synapse;
    Task DeliverAsync<T>(T synapse) where T : Synapse;
    TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey;
}
