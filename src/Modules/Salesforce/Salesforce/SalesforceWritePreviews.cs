using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Salesforce;

// Volatile, immutable proposals: the model can propose a change but cannot grant permission.
internal sealed partial class SalesforceWritePreviews : ITrustedUserCommandHandler
{
    private readonly Dictionary<string, Preview> _previews = new(StringComparer.Ordinal);
    private readonly Action<SalesforceInvocation> _authorize;
    private readonly Func<SalesforceInvocation, string, string, string?, JsonElement, CancellationToken, Task<object?>> _submit;
    private readonly IUntrustedContentScreen _screen;
    private readonly TimeProvider _time;

    internal SalesforceWritePreviews(SalesforceMcp mcp, IUntrustedContentScreen screen)
        : this(identity => mcp.Authorize(identity, identity.Binding), mcp.SubmitAsync, screen, TimeProvider.System) { }

    internal SalesforceWritePreviews(Action<SalesforceInvocation> authorize,
        Func<SalesforceInvocation, string, string, string?, JsonElement, CancellationToken, Task<object?>> submit,
        IUntrustedContentScreen screen, TimeProvider time)
        => (_authorize, _submit, _screen, _time) = (authorize, submit, screen, time);

    internal async Task<string> CreateAsync(SalesforceInvocation identity, AIFunction native,
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var context = AgentTurnContext.Current;
        if (context is null || context.Actor.PrincipalId != VerifiedActor.Current?.PrincipalId || context.Actor.PrincipalId != identity.Principal
            || context.Chat.Owner != identity.Agent.Owner || context.AllowedToolNames is not null
            || native.Name is not ("createRecord" or "updateRecord"))
        {
            throw new McpOperationException("Salesforce changes require a fresh authenticated chat request and exact preview. Login cannot approve a write.");
        }
        _authorize(identity);
        var payload = JsonSerializer.SerializeToElement(arguments);
        if (payload.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(payload.GetRawText()) > 24 * 1024)
        {
            throw new McpOperationException("The complete Salesforce change must fit within 24 KiB.");
        }
        await _screen.ScreenAsync(payload.GetRawText(), cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_previews)
        {
            foreach (var id in _previews.Where(pair => pair.Value.ExpiresAt <= _time.GetUtcNow()).Select(pair => pair.Key).ToArray())
            {
                _previews.Remove(id);
            }
            // One full, application-published proposal per initiating command. A subsequent model
            // call cannot replace the reviewed operation or silently append a second mutation.
            var existing = _previews.Values.FirstOrDefault(preview => SameTurn(preview.Context, context));
            if (existing is not null) { return existing.Response; }
            if (_previews.Count >= 128) { throw new McpOperationException("Too many Salesforce previews. Wait for an existing preview to expire."); }
            var proposalId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var preview = new Preview(context, identity, native.Name, native.JsonSchema.GetRawText(),
                native.ReturnJsonSchema?.GetRawText(), payload, _time.GetUtcNow().AddMinutes(10), proposalId);
            if (Encoding.UTF8.GetByteCount(preview.Response) > 32 * 1024)
            {
                throw new McpOperationException("The full Salesforce preview exceeds 32 KiB. Shorten the change.");
            }
            _previews.Add(proposalId, preview);
            return preview.Response;
        }
    }

    public string? ResponseFor(AgentTurnContext context)
    {
        lock (_previews)
        {
            return _previews.Values.FirstOrDefault(preview => SameTurn(preview.Context, context)
                && !preview.Consumed && preview.ExpiresAt > _time.GetUtcNow())?.Response;
        }
    }

    public void ResponsePublished(AgentTurnContext context, string response)
    {
        lock (_previews)
        {
            foreach (var preview in _previews.Values.Where(preview => SameTurn(preview.Context, context)
                && !preview.Consumed && preview.Response == response && preview.ExpiresAt > _time.GetUtcNow()))
            {
                preview.Published = true;
            }
        }
    }

    public async Task<string?> HandleAsync(string originalUserText, CancellationToken cancellationToken)
    {
        var match = Confirmation().Match(originalUserText);
        if (!match.Success) { return null; }
        var context = AgentTurnContext.Current;
        if (context is null || context.Actor.PrincipalId != VerifiedActor.Current?.PrincipalId || context.AllowedToolNames is not null)
        {
            return "Salesforce confirmation must be a new authenticated user message after reviewing the exact preview.";
        }
        Preview preview;
        lock (_previews)
        {
            if (!_previews.TryGetValue(match.Groups[1].Value, out preview!) || !preview.Published
                || preview.Context.Chat != context.Chat || preview.Context.Actor != context.Actor
                || preview.Context.CommandId == context.CommandId || preview.ExpiresAt <= _time.GetUtcNow())
            {
                return "This Salesforce preview is unavailable or expired. Request and review a fresh preview before confirming.";
            }
            if (preview.Consumed) { return preview.Result; }
            try { _authorize(preview.Identity); }
            catch (McpAuthenticationRequiredException)
            {
                return "Reconnect Salesforce, then request and confirm a fresh preview. This confirmation performed no write.";
            }
            catch (McpOperationException)
            {
                return "The Salesforce account binding changed. Request and confirm a fresh preview. This confirmation performed no write.";
            }
            // Consume BEFORE any discovery, network operation or possible cancellation. This
            // tombstone remains through expiry; duplicate messages cannot replay the mutation.
            preview.Consumed = true;
        }
        try
        {
            var result = await _submit(preview.Identity, preview.ToolName, preview.Schema, preview.ResultSchema,
                preview.Arguments, cancellationToken).ConfigureAwait(false);
            if (result is JsonElement element) { result = McpEvidencePreview.Redact(element); }
            var json = JsonSerializer.Serialize(result);
            await _screen.ScreenAsync(json, cancellationToken).ConfigureAwait(false);
            lock (_previews)
            {
                preview.Result = McpDiscoveredTool.IsError(result)
                    ? "Salesforce returned an error for the submitted change. Check the record before proposing another change; this preview will not be retried. " + json
                    : "Salesforce change submitted. " + json;
            }
        }
        catch (Exception)
        {
            lock (_previews)
            {
                preview.Result = "The Salesforce submission outcome could not be confirmed. This preview will never be retried. Check Salesforce before requesting a new preview.";
            }
        }
        return preview.Result;
    }

    private static bool SameTurn(AgentTurnContext a, AgentTurnContext b)
        => a.Chat == b.Chat && a.Actor == b.Actor && a.CommandId == b.CommandId;

    [GeneratedRegex(@"\Aconfirm salesforce change ([a-f0-9]{64})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Confirmation();

    private sealed class Preview(AgentTurnContext context, SalesforceInvocation identity, string toolName,
        string schema, string? resultSchema, JsonElement arguments, DateTimeOffset expiresAt, string proposalId)
    {
        internal AgentTurnContext Context { get; } = context;
        internal SalesforceInvocation Identity { get; } = identity;
        internal string ToolName { get; } = toolName;
        internal string Schema { get; } = schema;
        internal string? ResultSchema { get; } = resultSchema;
        internal JsonElement Arguments { get; } = arguments;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal bool Published;
        internal bool Consumed;
        internal string Result = "This Salesforce preview was already submitted. Check Salesforce; it will not be retried.";
        internal string Response { get; } = CreateResponse(toolName, arguments, expiresAt, proposalId);

        private static string CreateResponse(string tool, JsonElement arguments, DateTimeOffset expires, string id)
        {
            var json = JsonSerializer.Serialize(arguments, new JsonSerializerOptions { WriteIndented = true });
            var longest = 0;
            var run = 0;
            foreach (var character in json)
            {
                run = character == '`' ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }
            var fence = new string('`', Math.Max(3, longest + 1));
            return $"Salesforce change preview — no record has been written.\n\nNative operation: {tool}\nExact arguments:\n{fence}json\n{json}\n{fence}\n\nPreview expires: {expires:O}\nTo submit exactly this change, send this exact message:\nconfirm salesforce change {id}";
        }
    }
}
