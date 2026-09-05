namespace DigitalBrain.Abstractions.Signals;

// Optional semantics for replaceable state. Ordinary event facts stay individually queued.
public interface IVersionedSignal
{
    string SubjectKey { get; }
    string Version { get; }
    DateTimeOffset? CreatedAt { get; }
    string CompletionKey => Version;
}
