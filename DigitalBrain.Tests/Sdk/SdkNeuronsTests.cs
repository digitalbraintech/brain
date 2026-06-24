using DigitalBrain.Protocol;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Sdk;

// Functional tests for the typed SDK integration neurons against the in-process TestCluster.
public class SdkNeuronsTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task Shell_Executes_Echo()
    {
        var shell = _cluster!.GrainFactory.GetGrain<IShellNeuron>("shell-test");
        var result = await shell.ExecuteAsync("echo digitalbrain");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("digitalbrain", result.Output);
    }

    [Fact]
    public async Task Shell_Blocks_Dangerous_Command()
    {
        var shell = _cluster!.GrainFactory.GetGrain<IShellNeuron>("shell-block");
        var result = await shell.ExecuteAsync("format c:");
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("blocked", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileSystem_Write_Read_List_Delete_RoundTrip()
    {
        var fs = _cluster!.GrainFactory.GetGrain<IFileSystemNeuron>("fs-test");
        var dir = Path.Combine(Path.GetTempPath(), "dbfs-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "note.txt");
        try
        {
            await fs.WriteFileAsync(file, "hello fs");
            Assert.True(await fs.ExistsAsync(file));
            Assert.Equal("hello fs", await fs.ReadFileAsync(file));
            Assert.Contains(file, await fs.ListFilesAsync(dir, "*.txt"));
            await fs.DeleteAsync(file);
            Assert.False(await fs.ExistsAsync(file));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task DotNet_Reports_Sdk_Version()
    {
        var dotnet = _cluster!.GrainFactory.GetGrain<IDotNetNeuron>("dotnet-test");
        var version = await dotnet.VersionAsync();
        Assert.Matches(@"\d+\.\d+", version);
    }

    [Fact]
    public async Task Git_Status_Works_After_ProcessRunner_Refactor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dbgit2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var init = new System.Diagnostics.ProcessStartInfo("git", "init -b main")
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = System.Diagnostics.Process.Start(init)!)
                process.WaitForExit();

            var git = _cluster!.GrainFactory.GetGrain<IGitNeuron>("git-smoke");
            var status = await git.StatusAsync(dir);
            Assert.Contains("branch", status, System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
    }
}
