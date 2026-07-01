using System.Diagnostics;
using DigitalBrain.TestKit;
using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class GitNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Status_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dbgit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var init = new ProcessStartInfo("git", "init -b main")
            { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
            using (var process = Process.Start(init)!) process.WaitForExit();

            var git = _brain.Grain<IGitNeuron>("git-smoke");
            var status = await git.StatusAsync(dir);
            Assert.Contains("branch", status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
