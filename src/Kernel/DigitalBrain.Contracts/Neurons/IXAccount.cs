using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

[Alias("db.x-account")]
public partial interface IXAccount : INeuron, IHandle<PublishPost>;
