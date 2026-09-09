using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Memory;

[Alias("DigitalBrain.Memory.IVectorMemory")]
public partial interface IVectorMemory :
    INeuron,
    IHandle<StoreVectorMemory>,
    IHandle<SearchVectorMemory>,
    IHandle<RemoveVectorMemory>;
