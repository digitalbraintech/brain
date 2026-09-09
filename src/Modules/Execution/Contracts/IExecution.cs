using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Execution;

[Alias("execution")]
public partial interface IExecution :
    INeuron,
    IHandle<StartExecution>,
    IHandle<ReadExecution>;
