using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Scripting.Startup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class StartupScriptWorkerTests
{
    [Fact]
    public async Task Duplicate_activation_executes_the_same_script_version_once()
    {
        var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
        try
        {
            var scriptPath = await WriteScriptAsync(directory, "return 1;");
            var source = new TestActivationSource(new StartupActivation("alice", "activation"), new StartupActivation("alice", "activation"));
            var runner = new TestRunner(new StartupScriptRunResult(true, "started", []));
            var ledger = new FileStartupExecutionLedger(directory.FullName);
            var worker = CreateWorker(source, runner, ledger, scriptPath);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await source.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await worker.StopAsync(TestContext.Current.CancellationToken);

            var script = await StartupScript.ReadAsync(scriptPath, TestContext.Current.CancellationToken);
            var execution = await ledger.FindAsync(
                new StartupExecutionKey("alice", "activation", script.Sha256),
                TestContext.Current.CancellationToken);

            Assert.Equal(1, runner.InvocationCount);
            Assert.Equal("alice", Assert.IsType<DigitalBrain.Abstractions.Signals.DigitalBrainActivated>(runner.LastScript!.Input).Owner.Value);
            Assert.True(execution?.IsSuccess);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Failed_execution_is_recorded_and_not_retried_automatically()
    {
        var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
        try
        {
            var scriptPath = await WriteScriptAsync(directory, "return 1;");
            var source = new TestActivationSource(new StartupActivation("alice", "activation"), new StartupActivation("alice", "activation"));
            var runner = new TestRunner(new StartupScriptRunResult(false, "Compilation failed.", ["CS1002"]));
            var ledger = new FileStartupExecutionLedger(directory.FullName);
            var worker = CreateWorker(source, runner, ledger, scriptPath);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await source.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await worker.StopAsync(TestContext.Current.CancellationToken);

            var script = await StartupScript.ReadAsync(scriptPath, TestContext.Current.CancellationToken);
            var execution = await ledger.FindAsync(
                new StartupExecutionKey("alice", "activation", script.Sha256),
                TestContext.Current.CancellationToken);

            Assert.Equal(1, runner.InvocationCount);
            Assert.NotNull(execution);
            Assert.False(execution.IsSuccess);
            Assert.Equal("Compilation failed.", execution.Summary);
            Assert.Equal(["CS1002"], execution.Diagnostics);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_changed_script_hash_can_run_for_the_same_activation()
    {
        var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
        try
        {
            var scriptPath = await WriteScriptAsync(directory, "return 2;");
            var previousScript = StartupScript.FromSource(scriptPath, "return 1;");
            var ledger = new FileStartupExecutionLedger(directory.FullName);
            await ledger.RecordAsync(
                StartupExecution.Succeeded(
                    new StartupExecutionKey("alice", "activation", previousScript.Sha256),
                    "started",
                    DateTimeOffset.UnixEpoch),
                TestContext.Current.CancellationToken);
            var source = new TestActivationSource(new StartupActivation("alice", "activation"));
            var runner = new TestRunner(new StartupScriptRunResult(true, "started", []));
            var worker = CreateWorker(source, runner, ledger, scriptPath);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await source.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await worker.StopAsync(TestContext.Current.CancellationToken);

            var currentScript = await StartupScript.ReadAsync(scriptPath, TestContext.Current.CancellationToken);
            var execution = await ledger.FindAsync(
                new StartupExecutionKey("alice", "activation", currentScript.Sha256),
                TestContext.Current.CancellationToken);

            Assert.Equal(1, runner.InvocationCount);
            Assert.True(execution?.IsSuccess);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_stops_execution_without_recording_a_false_completion()
    {
        var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
        try
        {
            var scriptPath = await WriteScriptAsync(directory, "return 1;");
            var source = new TestActivationSource(new StartupActivation("alice", "activation"));
            var runner = new CancellingRunner();
            var ledger = new FileStartupExecutionLedger(directory.FullName);
            var worker = CreateWorker(source, runner, ledger, scriptPath);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await runner.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await worker.StopAsync(TestContext.Current.CancellationToken);
            await runner.Cancelled.Task.WaitAsync(TestContext.Current.CancellationToken);

            var script = await StartupScript.ReadAsync(scriptPath, TestContext.Current.CancellationToken);
            var execution = await ledger.FindAsync(
                new StartupExecutionKey("alice", "activation", script.Sha256),
                TestContext.Current.CancellationToken);

            Assert.Null(execution);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Missing_script_is_logged_as_a_worker_error_without_a_terminal_receipt()
    {
        var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
        try
        {
            var scriptPath = Path.Combine(directory.FullName, "missing.cs");
            var source = new TestActivationSource(
                new StartupActivation("alice", "missing"),
                new StartupActivation("alice", "fixed"))
            {
                PauseAfterFirst = true,
            };
            var runner = new TestRunner(new StartupScriptRunResult(true, "started", []));
            var ledger = new FileStartupExecutionLedger(directory.FullName);
            var logger = new ListLogger<StartupScriptWorker>();
            var worker = CreateWorker(source, runner, ledger, scriptPath, logger);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await logger.ErrorLogged.Task.WaitAsync(TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(scriptPath, "return 1;", TestContext.Current.CancellationToken);
            source.Release.TrySetResult();
            await source.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await worker.StopAsync(TestContext.Current.CancellationToken);

            var script = await StartupScript.ReadAsync(scriptPath, TestContext.Current.CancellationToken);
            var execution = await ledger.FindAsync(
                new StartupExecutionKey("alice", "fixed", script.Sha256),
                TestContext.Current.CancellationToken);

            Assert.Equal(1, runner.InvocationCount);
            Assert.True(execution?.IsSuccess);
            var error = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Error);
            Assert.Equal(LogLevel.Error, error.Level);
            Assert.Equal("alice", error.Properties["Owner"]);
            Assert.Equal("missing", error.Properties["ActivationSignalId"]);
            Assert.Equal(scriptPath, error.Properties["ScriptPath"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static StartupScriptWorker CreateWorker(
        TestActivationSource source,
        IStartupScriptRunner runner,
        IStartupExecutionLedger ledger,
        string scriptPath,
        ILogger<StartupScriptWorker>? logger = null) => new(
            source,
            runner,
            ledger,
            new FakeDigitalBrain("alice"),
            Options.Create(new StartupScriptOptions { ScriptPath = scriptPath }),
            TimeProvider.System,
            logger ?? NullLogger<StartupScriptWorker>.Instance);

    private static async Task<string> WriteScriptAsync(DirectoryInfo directory, string source)
    {
        var path = Path.Combine(directory.FullName, "start.cs");
        await File.WriteAllTextAsync(path, source, TestContext.Current.CancellationToken);
        return path;
    }

    private sealed class TestActivationSource(params StartupActivation[] activations) : IStartupActivationSource
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool PauseAfterFirst { get; init; }

        public IAsyncEnumerable<StartupActivation> WatchAsync(CancellationToken cancellationToken)
            => Watch(activations, cancellationToken);

        private async IAsyncEnumerable<StartupActivation> Watch(
            IEnumerable<StartupActivation> activations,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            try
            {
                for (var index = 0; index < activations.Count(); index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return activations.ElementAt(index);

                    if (PauseAfterFirst && index == 0)
                    {
                        await Release.Task.WaitAsync(cancellationToken);
                    }
                }
            }
            finally
            {
                Completed.TrySetResult();
            }
        }
    }

    private sealed class TestRunner(StartupScriptRunResult result) : IStartupScriptRunner
    {
        public int InvocationCount { get; private set; }
        public StartupScript? LastScript { get; private set; }

        public Task<StartupScriptRunResult> RunAsync(
            StartupScript script,
            IDigitalBrain brain,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastScript = script;
            return Task.FromResult(result);
        }
    }

    private sealed class CancellingRunner : IStartupScriptRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StartupScriptRunResult> RunAsync(
            StartupScript script,
            IDigitalBrain brain,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("Cancellation did not stop the runner.");
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public TaskCompletionSource ErrorLogged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => EmptyScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(static value => value.Key, static value => value.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, properties));
            if (logLevel == LogLevel.Error)
            {
                ErrorLogged.TrySetResult();
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Properties);

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
