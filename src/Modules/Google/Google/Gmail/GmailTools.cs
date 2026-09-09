using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Google;

internal sealed class GmailTools(GmailMcp gmail, GmailConnections connections,
    GmailLogins logins, GmailDraftPreviews previews, IUntrustedContentScreen screen) : IAgentToolSource
{
    private readonly GmailMcp _gmail = gmail;
    private readonly GmailConnections _connections = connections;
    private readonly GmailDraftPreviews _previews = previews;

    public async ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = RequireIdentity(context);
        var operation = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        try
        {
            var binding = _connections.Identity(context.Owner, identity.Principal);
            RequireContinuation(context, binding);
            var native = await _gmail.GetToolsAsync(identity, cancellationToken).ConfigureAwait(true);
            if (AgentTurnContext.Current?.AllowedToolNames is { } admitted
                && admitted.Any(name => name != "get_current_account" && !native.Any(tool => tool.Name == name)))
            {
                throw new McpOperationException("The Gmail read catalog changed. Send a fresh request.", McpFailureKind.CatalogChanged);
            }
            await context.ObserveAsync(new AgentActivity(operation, "tool", "completed", "tools/list", Server: "gmail",
                DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Preview: McpEvidencePreview.Create(string.Join(", ", native.Select(tool => tool.Name))))).ConfigureAwait(true);
            return [AccountTool(context, identity), .. native.Select(tool => AgentToolExecution.Observe(context,
                new PolicyTool(tool, this, context, identity, binding), "gmail", screen))];
        }
        catch (McpAuthenticationRequiredException)
        {
            // Native schema discovery needs a credential. This small local tool creates the
            // existing browser action; no fabricated provider schemas or general delegation.
            if (AgentTurnContext.Current?.AllowedToolNames is not null)
            {
                throw new McpOperationException("Gmail authorization did not complete this read. Send a new request after checking access.");
            }
            await context.ObserveAsync(new AgentActivity(operation, "tool", "failed", "tools/list", Server: "gmail",
                IsError: true, FailureCode: "authentication_required")).ConfigureAwait(true);
            return [AccountTool(context, identity)];
        }
    }

    private AIFunction AccountTool(AgentToolContext context, GmailAgentIdentity identity)
        => AIFunctionFactory.Create((CancellationToken cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireActive();
            try
            {
                var binding = _connections.Identity(context.Owner, identity.Principal);
                GmailMcp.Authorize(identity, binding);
                RequireContinuation(context, binding);
                return JsonSerializer.Serialize(new
                {
                    email = binding.Email,
                    canCompose = binding.CanCompose,
                    status = "cached_account",
                    message = "Selected authorized account. Cached identity does not verify live Gmail reachability."
                });
            }
            catch (McpAuthenticationRequiredException) { return Login(false, cancellationToken); }
        }, new AIFunctionFactoryOptions
        {
            Name = "get_current_account",
            Description = "Check the selected authorized Google account. If disconnected, creates the application's browser login action. Cached identity alone does not prove Gmail is reachable. Never asks for credentials in chat."
        });

    internal static GmailAgentIdentity RequireIdentity(AgentToolContext context)
    {
        context.RequireActive();
        if (context.Principal is not { } principal || VerifiedActor.Current?.PrincipalId != principal
            || context.Agent.Type != "gmail" || !PrincipalPartition.OwnsInstance(principal, context.Agent.Name))
        {
            throw new McpOperationException("Gmail requires the current authenticated specialist.", McpFailureKind.AccessDenied);
        }
        return new(context.Agent, principal);
    }

    internal static void RequireContinuation(AgentToolContext context, GmailIdentity binding)
    {
        if (AgentTurnContext.Current?.AllowedToolNames is not { } allowed)
        {
            return;
        }
        var continuation = AgentTurnContext.Current.SpecialistContinuation;
        if (continuation is null || continuation.Target != context.Agent
            || continuation.ConnectionRevision != binding.Revision.ToString("N")
            || !new HashSet<string>(allowed, StringComparer.Ordinal).SetEquals(GmailLogins.ReadTools)
            || !new HashSet<string>(continuation.AllowedToolNames, StringComparer.Ordinal).SetEquals(allowed))
        {
            throw new McpOperationException("The Gmail continuation target, connection or read permissions changed. Send a fresh request.", McpFailureKind.ConnectionChanged);
        }
    }

    private string Login(bool compose, CancellationToken cancellationToken)
    {
        try
        {
            var action = logins.RequireLogin(compose, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                status = "authentication_required",
                actionId = action.Id,
                message = "Use the application's Gmail login action. Do not request secrets, invent a URL or retry tools. Reads resume once; drafts require a fresh preview and explicit confirmation after login."
            });
        }
        catch (McpOperationException error)
        {
            return JsonSerializer.Serialize(new { status = "unavailable", message = error.Message });
        }
    }

    private sealed class PolicyTool(AIFunction native, GmailTools source, AgentToolContext context,
        GmailAgentIdentity identity, GmailIdentity binding) : DelegatingAIFunction(native)
    {
        public override string Description => Name == "create_draft"
            ? base.Description + " APPLICATION POLICY: prepares an exact preview only; creates nothing. A fresh trusted user confirmation of the published preview is required to create the draft."
            : base.Description;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            context.RequireActive();
            RequireIdentity(context);
            RequireContinuation(context, binding);
            if (AgentTurnContext.Current?.AllowedToolNames is { } admitted && !admitted.Contains(Name, StringComparer.Ordinal))
            {
                throw new McpOperationException("This Gmail operation is not admitted during login continuation.", McpFailureKind.AccessDenied);
            }
            try
            {
                if (source._connections.Identity(context.Owner, identity.Principal) != binding)
                {
                    throw new McpOperationException("The Gmail connection changed. Request fresh tools and a new preview.", McpFailureKind.ConnectionChanged);
                }
                var args = GmailContent.Normalize(Name, new Dictionary<string, object?>(arguments));
                if (Name == "create_draft")
                {
                    await source._previews.CreateAsync(context.Owner, (string[])args["to"]!, (string[])args["cc"]!,
                        (string[])args["bcc"]!, (string)args["subject"]!, (string)args["body"]!, cancellationToken, identity, InnerFunction).ConfigureAwait(true);
                    // Confirmation commands are application control data. The exact preview is
                    // published through the trusted chat response path, never a model tool result.
                    return JsonSerializer.SerializeToElement(new
                    {
                        status = "preview_ready",
                        message = "The application will display the complete draft preview.",
                    });
                }
                return await source._gmail.InvokeAsync(identity, InnerFunction, args, cancellationToken).ConfigureAwait(true);
            }
            catch (McpAuthenticationRequiredException) { return source.Login(Name == "create_draft", cancellationToken); }
        }
    }
}

