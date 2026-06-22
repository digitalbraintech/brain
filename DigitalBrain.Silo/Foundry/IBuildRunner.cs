using System.Diagnostics;

namespace DigitalBrain.Silo.Foundry;

public record BuildOutcome(bool Success, string Log);

public interface IBuildRunner
{
    Task<BuildOutcome> VerifyBuildAsync(string moduleName, string source);
}

public sealed class ProcessBuildRunner : IBuildRunner
{
    public async Task<BuildOutcome> VerifyBuildAsync(string moduleName, string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "foundry-verify-" + moduleName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, moduleName + ".cs"), source);
            var siloProject = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "DigitalBrain.Silo.csproj"));
            if (!File.Exists(siloProject))
                siloProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "DigitalBrain.Silo.csproj"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "verify.csproj"), VerifyProject(siloProject));

            var psi = new ProcessStartInfo("dotnet", $"build \"{Path.Combine(tempDir, "verify.csproj")}\" -c Release")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync();
            return new BuildOutcome(process.ExitCode == 0, stdoutTask.Result + stderrTask.Result);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static string VerifyProject(string siloProjectPath) => $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{siloProjectPath}" />
  </ItemGroup>
</Project>
""";
}
