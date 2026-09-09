using System.ComponentModel;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

// Modules contribute AI tools without the AI module referencing them (IAgentToolSource,
// Task 4). This is the UI module's contribution: render_chart is always offered;
// generate_image only appears once an IImageGeneration provider is actually configured
// (the honesty gate UIModule.Configure applies via sp.GetService<IImageGeneration>()).
internal sealed class KitToolSource(
    IGrainFactory grains,
    IImageGeneration? imageGeneration,
    IKitImageStore imageStore) : IAgentToolSource
{
    public ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.RequireActive();
        return ValueTask.FromResult<IReadOnlyList<AITool>>(CreateTools(context.Owner));
    }

    private IReadOnlyList<AIFunction> CreateTools(OwnerId owner)
    {
        // The model only ever echoes back a chatName it read from its own conversation
        // context, so the two local wrapper functions below close over the trusted `owner`
        // from this call rather than accepting it as a tool parameter (which would let the
        // model supply -- and forge -- it). RenderChartAsync/GenerateImageAsync then refuse
        // any chatName that isn't scoped under this owner before touching a grain.
        Task<string> RenderChart(
            [Description("The current chat's name, exactly as stated in the conversation context")] string chatName,
            [Description("Short chart title")] string title,
            [Description("bar or line")] string chartKind,
            [Description("Point labels, one per value")] string[] labels,
            [Description("Point values, one per label")] double[] values,
            CancellationToken cancellationToken)
            => RenderChartAsync(owner, chatName, title, chartKind, labels, values, cancellationToken);

        Task<string> ShowGraph(
            [Description("The current chat's name, exactly as stated in the conversation context")] string chatName,
            [Description("Short graph title")] string title,
            [Description("Node ids, one per node. The first id is the graph's centre")] string[] nodeIds,
            [Description("Node labels, one per node id, in the same order")] string[] nodeLabels,
            [Description("Edges as 'sourceId>targetId', one per edge")] string[] edges,
            CancellationToken cancellationToken)
            => ShowGraphAsync(owner, chatName, title, nodeIds, nodeLabels, edges, cancellationToken);

        var tools = new List<AIFunction>
        {
            AIFunctionFactory.Create(RenderChart, new AIFunctionFactoryOptions
            {
                Name = "render_chart",
                Description = "Render a chart for the owner. It appears as a live card in the chat and can "
                    + "be shown on surfaces later. Use it whenever the owner asks to see data as a chart.",
            }),
            AIFunctionFactory.Create(ShowGraph, new AIFunctionFactoryOptions
            {
                Name = "show_graph",
                Description = "Render a navigable graph for the owner. It appears as a live card in the "
                    + "chat and can be shown on surfaces later. Use it whenever the owner asks to see how "
                    + "things connect, relate, or depend on each other.",
            }),
        };

        if (imageGeneration is not null)
        {
            Task<string> GenerateImage(
                [Description("The current chat's name, exactly as stated in the conversation context")] string chatName,
                [Description("What the image should depict")] string prompt,
                CancellationToken cancellationToken)
                => GenerateImageAsync(owner, chatName, prompt, cancellationToken);

            tools.Add(AIFunctionFactory.Create(GenerateImage, new AIFunctionFactoryOptions
            {
                Name = "generate_image",
                Description = "Generate an image from a text prompt and show it as a card in the chat. "
                    + "Use it whenever the owner asks for a picture, illustration, or image.",
            }));
        }

        return tools;
    }

    // Rejects a chatName the model echoed back that doesn't belong to this owner's
    // partition, before either tool touches a grain.
    private static string? OwnerGuardError(OwnerId owner, string chatName)
    {
        var ownerPrefix = $"{owner.Value}/";
        return chatName.StartsWith(ownerPrefix, StringComparison.Ordinal)
            ? null
            : $"chatName must be a chat key of this owner (starting with '{ownerPrefix}').";
    }

    private async Task<string> ShowGraphAsync(
        OwnerId owner,
        string chatName,
        string title,
        string[] nodeIds,
        string[] nodeLabels,
        string[] edges,
        CancellationToken cancellationToken)
    {
        try
        {
            if (OwnerGuardError(owner, chatName) is { } ownerError)
            {
                return ownerError;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return "title must not be blank.";
            }

            if (nodeIds.Length == 0)
            {
                return "nodeIds must contain at least one node.";
            }

            if (nodeIds.Length != nodeLabels.Length)
            {
                return "nodeIds and nodeLabels must have the same length.";
            }

            var known = nodeIds.Select(static id => id.Trim()).ToHashSet(StringComparer.Ordinal);

            // The first id anchors the graph's centre; every other node sits on the shell.
            var nodes = nodeIds
                .Select((id, index) => new GraphNodeState(
                    id.Trim(),
                    nodeLabels[index].Trim(),
                    index == 0 ? GraphNodeKinds.Hub : GraphNodeKinds.Leaf))
                .ToArray();

            var parsed = new List<GraphEdgeState>();
            foreach (var edge in edges)
            {
                var parts = edge.Split('>', 2);
                if (parts.Length != 2
                    || !known.Contains(parts[0].Trim())
                    || !known.Contains(parts[1].Trim()))
                {
                    return $"edge '{edge}' must be 'sourceId>targetId' using ids from nodeIds.";
                }

                var source = parts[0].Trim();
                var target = parts[1].Trim();
                parsed.Add(new GraphEdgeState($"{source}-{target}", source, target));
            }

            var trimmedTitle = title.Trim();
            var name = $"graph-{Guid.NewGuid():N}"[..14];
            var instance = KitInstanceNames.Sibling(chatName, name);

            await grains.GetGrain<IGraph>(instance)
                .Render(new GraphState(trimmedTitle, nodes, parsed));
            await grains.GetGrain<IChat>(chatName)
                .HandleAsync(new KitCardOffer(KitCardKinds.Graph, name, trimmedTitle), cancellationToken);

            return $"Graph '{trimmedTitle}' is now showing in the chat as card '{name}'.";
        }
        catch (Exception ex)
        {
            return $"show_graph failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task<string> RenderChartAsync(
        OwnerId owner,
        string chatName,
        string title,
        string chartKind,
        string[] labels,
        double[] values,
        CancellationToken cancellationToken)
    {
        try
        {
            if (OwnerGuardError(owner, chatName) is { } ownerError)
            {
                return ownerError;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return "title must not be blank.";
            }

            labels ??= [];
            values ??= [];
            if (labels.Length == 0 || labels.Length != values.Length)
            {
                return "labels and values must be non-empty and the same length.";
            }

            var trimmedTitle = title.Trim();
            var kind = string.IsNullOrWhiteSpace(chartKind) ? "bar" : chartKind.Trim();
            var name = $"chart-{Guid.NewGuid():N}"[..14];
            var instance = KitInstanceNames.Sibling(chatName, name);
            var points = labels.Zip(values, static (label, value) => new ChartPoint(label, value)).ToList();

            await grains.GetGrain<IChart>(instance).Render(new ChartState(trimmedTitle, kind, points));
            await grains.GetGrain<IChat>(chatName)
                .HandleAsync(new KitCardOffer(KitCardKinds.Chart, name, trimmedTitle), cancellationToken);

            return $"Chart '{trimmedTitle}' is now showing in the chat as card '{name}'.";
        }
        catch (Exception ex)
        {
            return $"render_chart failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task<string> GenerateImageAsync(
        OwnerId owner,
        string chatName,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (OwnerGuardError(owner, chatName) is { } ownerError)
        {
            return ownerError;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "prompt must not be blank.";
        }

        var trimmedPrompt = prompt.Trim();
        var generated = await imageGeneration!.GenerateAsync(trimmedPrompt, cancellationToken);

        var name = $"image-{Guid.NewGuid():N}"[..14];
        var blobName = $"{name}.png";
        await imageStore.SaveAsync(blobName, generated.Content, generated.MediaType, cancellationToken);

        var instance = KitInstanceNames.Sibling(chatName, name);
        await grains.GetGrain<IImage>(instance)
            .Describe(new ImageState(trimmedPrompt, generated.Model, generated.MediaType, blobName));
        await grains.GetGrain<IChat>(chatName)
            .HandleAsync(new KitCardOffer(KitCardKinds.Image, name, trimmedPrompt), cancellationToken);

        return $"Image for '{trimmedPrompt}' is now showing in the chat as card '{name}'.";
    }
}
