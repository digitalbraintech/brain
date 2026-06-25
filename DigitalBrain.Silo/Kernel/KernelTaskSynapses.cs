namespace DigitalBrain.Silo;

using DigitalBrain.Core;

// Kernel task grain contract (kernel-owned). The synapse records (protocol messages) live in Core.
public interface IKernelTask : INeuron, IHandle<RunKernelTask>, IHandle<CancelKernelTask>
{
    Task<KernelTaskInfo> GetInfoAsync();
}
