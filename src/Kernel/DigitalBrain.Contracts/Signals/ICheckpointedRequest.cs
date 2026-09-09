namespace DigitalBrain.Abstractions.Signals;

// A completed result is stable for the lifetime of an invocation, including retries.
// Read requests omit this marker so retries obtain current external evidence.
public interface ICheckpointedRequest;
