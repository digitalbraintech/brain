namespace DigitalBrain.Abstractions.Signals;

// Marks a signal that reaches its audience by journal projection rather than by delivery. The
// UI reads chat.responded from the chat's outgoing feed, so resolving zero receivers is normal
// for it and must not be reported as an undelivered signal.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JournalProjectionAttribute : Attribute;
