namespace DigitalBrain.Core;

public sealed record ApplicationNeuronEventRegistration(
    string NeuronType,
    string EventKey,
    string Contract,
    Type SignalType,
    bool IsPublic = false);

public sealed record ApplicationNeuronInputRegistration(
    string NeuronType,
    string Contract,
    Type SignalType,
    string? InputKey = null,
    Type? ResponseType = null,
    bool IsPublic = false);

public sealed record ApplicationNeuronCapabilityRegistration(
    string NeuronType,
    Type ContractType,
    string Key,
    string DefaultInstanceName,
    string ProjectPath);
