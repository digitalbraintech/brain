using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Prepared tool execution owns evidence and content policy; Agent owns the model turn.
public static class AgentToolExecution
{
    public static AIFunction Observe(
        AgentToolContext context, AIFunction function, string server, IUntrustedContentScreen screen)
        => new ObservedTool(context, function, server, screen);

    private sealed class ObservedTool(
        AgentToolContext context, AIFunction function, string server, IUntrustedContentScreen screen)
        : DelegatingAIFunction(function)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            context.RequireActive();
            var operation = Guid.NewGuid();
            var started = Stopwatch.GetTimestamp();
            await context.ObserveAsync(new AgentActivity(operation, "tool", "started", Name, Server: server)).ConfigureAwait(true);
            var state = "failed";
            string? preview = null;
            string? failureCode = null;
            var isError = false;
            var truncated = false;
            try
            {
                var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(true);
                if (result is JsonElement nativeResult)
                {
                    result = McpEvidencePreview.Redact(nativeResult);
                }
                var json = result is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(result);
                try
                {
                    await screen.ScreenAsync(json, cancellationToken).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    failureCode = "content_rejected";
                    throw new McpOperationException("The tool content did not pass screening. No successful observation is available.");
                }
                preview = McpEvidencePreview.Create(json);
                isError = McpDiscoveredTool.IsError(result);
                truncated = McpDiscoveredTool.IsTruncated(result);
                state = isError ? "failed" : "completed";
                failureCode = isError ? "tool_error" : null;
                return result;
            }
            catch (OperationCanceledException)
            {
                state = "cancelled";
                failureCode = "cancelled";
                throw;
            }
            catch (McpAuthenticationRequiredException)
            {
                failureCode = "authentication_required";
                throw;
            }
            catch (TimeoutException)
            {
                failureCode = "timeout";
                throw;
            }
            catch (McpOperationException error)
            {
                // SDK/provider operation exceptions carry deliberately safe messages.
                failureCode ??= error.Kind switch
                {
                    McpFailureKind.CatalogChanged => "catalog_changed",
                    McpFailureKind.ConnectionChanged => "connection_changed",
                    McpFailureKind.AccessDenied => "access_denied",
                    McpFailureKind.ContentRejected => "content_rejected",
                    McpFailureKind.Capacity => "capacity",
                    _ => "unavailable",
                };
                throw;
            }
            catch (Exception)
            {
                failureCode = "unavailable";
                throw new McpOperationException("The tool is unavailable. No successful observation is available.");
            }
            finally
            {
                Activity.Current?.SetTag("db.tool.failure_code", failureCode);
                await context.ObserveAsync(new AgentActivity(operation, "tool", state, Name, Server: server,
                    DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds, Preview: preview,
                    IsError: isError || state == "failed", Truncated: truncated, FailureCode: failureCode)).ConfigureAwait(true);
            }
        }
    }
}
