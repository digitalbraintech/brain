using DigitalBrain.Execution;

namespace DigitalBrain.Chat;

// In-silo return-value surface. Owners and scripts use IChat + SendAsync/RequestAsync.
// Commands stay on IChat.HandleAsync; nested RequestAsync deadlocks the session neuron.
[Alias("chat.runtime")]
public interface IChatKernel : IGrainWithStringKey
{
    Task<ChatTranscript> LoadTranscript();

    Task<IReadOnlyList<ChatTurnSnapshot>> LoadTurnSnapshots();

    Task<ExecutionId?> LoadActiveExecution();
}
