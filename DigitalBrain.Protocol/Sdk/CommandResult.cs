namespace DigitalBrain.Protocol;

// Result of a process/command execution by an SDK integration neuron (Shell/DotNet/NuGet/Winget).
// A non-zero ExitCode is legitimate data (build failure, conflict); a failure to START the process throws.
[GenerateSerializer]
public record CommandResult(
    [property: Id(0)] int ExitCode,
    [property: Id(1)] string Output,
    [property: Id(2)] string Error,
    [property: Id(3)] TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
}
