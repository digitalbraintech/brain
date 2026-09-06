using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Scripting.Startup;

internal sealed class StartupScriptWorker(
    IStartupActivationSource activationSource,
    IStartupScriptRunner runner,
    IStartupExecutionLedger ledger,
    IDigitalBrain brain,
    IOptions<StartupScriptOptions> options,
    TimeProvider timeProvider,
    ILogger<StartupScriptWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var activation in activationSource
            .WatchAsync(stoppingToken)
            .WithCancellation(stoppingToken))
        {
            StartupScript script;
            try
            {
                script = await StartupScript.ReadAsync(options.Value.ScriptPath, stoppingToken);
                script = script with { Input = new DigitalBrainActivated(new OwnerId(activation.Owner)) };
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Startup script could not be loaded for owner {Owner}, activation {ActivationSignalId}, path {ScriptPath}",
                    activation.Owner,
                    activation.SignalId,
                    options.Value.ScriptPath);
                continue;
            }

            var key = new StartupExecutionKey(activation.Owner, activation.SignalId, script.Sha256);
            if (await ledger.FindAsync(key, stoppingToken) is not null)
            {
                logger.LogDebug(
                    "Startup script execution already recorded for owner {Owner}, activation {ActivationSignalId}, script hash {ScriptSha256}",
                    activation.Owner,
                    activation.SignalId,
                    script.Sha256);
                continue;
            }

            var result = await RunAsync(script, stoppingToken);
            var completedAt = timeProvider.GetUtcNow();
            var execution = result.IsSuccess
                ? StartupExecution.Succeeded(key, result.Summary, completedAt)
                : StartupExecution.Failed(key, result.Summary, result.Diagnostics, completedAt);

            await ledger.RecordAsync(execution, stoppingToken);

            if (execution.IsSuccess)
            {
                logger.LogInformation(
                    "Startup script execution succeeded for owner {Owner}, activation {ActivationSignalId}, script hash {ScriptSha256}: {Summary}",
                    activation.Owner,
                    activation.SignalId,
                    script.Sha256,
                    execution.Summary);
            }
            else
            {
                logger.LogError(
                    "Startup script execution failed for owner {Owner}, activation {ActivationSignalId}, script hash {ScriptSha256}: {Summary}",
                    activation.Owner,
                    activation.SignalId,
                    script.Sha256,
                    execution.Summary);
            }
        }
    }

    private async Task<StartupScriptRunResult> RunAsync(StartupScript script, CancellationToken stoppingToken)
    {
        try
        {
            return await runner.RunAsync(script, brain, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new StartupScriptRunResult(false, exception.Message, []);
        }
    }
}
