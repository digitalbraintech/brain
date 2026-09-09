namespace DigitalBrain.Abstractions.Scripting;

public sealed record ExternalOrleansGateway(
    string Address,
    int Port,
    string ClusterId,
    string ServiceId);
