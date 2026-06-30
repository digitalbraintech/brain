using DigitalBrain.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalBrain.Tests.Ui;

/// <summary>
/// Fast, in-memory, type-safe harness for testing <see cref="KitExperience"/> (and by extension
/// the ui:/neuron: widget trees they emit).
///
/// This is the primary tool for fast iteration on the UI kit.
/// No browser, no Aspire, no RFW — just the model.
///
/// Use <see cref="LiveRenderVerifier"/> (or the old ExperienceFlowDriver) when you need to
/// prove that the live Flutter client (RFW + ui_kit + ForUI) actually turned the tree
/// into visible/accessible widgets.
/// </summary>
/// <typeparam name="TExperience">A KitExperience subclass</typeparam>
public sealed class ExperienceTestHarness<TExperience> where TExperience : KitExperience, new()
{
    private readonly TExperience _experience = new();
    private readonly Dictionary<string, string> _state = new();

    /// <summary>
    /// Sends an ExperienceStep (simulating a user action from the rendered UI).
    /// Returns the surface that would be emitted for the target hop.
    /// </summary>
    public UiSurface Trigger(string eventName, params (string key, string value)[] args)
    {
        var step = new ExperienceStep(_experience.GetType().Name, _experience.GetType().Name, eventName,
            args.ToDictionary(a => a.key, a => a.value));

        var outputs = _experience.Handle(step);
        return (UiSurface)outputs.FirstOrDefault(s => s is UiSurface) 
               ?? throw new InvalidOperationException($"No UiSurface emitted for step {eventName}");
    }

    /// <summary>
    /// Gets the UiWidgetTree for a specific hop using the current accumulated state.
    /// Useful for deep assertions on the declarative structure.
    /// </summary>
    public UiWidgetTree GetTreeForHop(string hopId)
    {
        // We re-trigger to let the experience build the tree for that hop.
        // This keeps the harness simple and faithful to the real KitExperience state machine.
        var surface = Trigger(hopId);
        if (surface.Props.TryGetValue("tree", out var t) && t is UiWidgetTree tree)
            return tree;

        // Fallback for non-tree surfaces (legacy)
        throw new NotSupportedException("Hop did not produce a widget tree. Use legacy surface assertions.");
    }

    public IReadOnlyDictionary<string, string> CurrentState => _state;
}

/// <summary>
/// Lightweight, type-safe assertions over UiWidgetTree.
/// Mirrors the spirit of React Testing Library / Flutter finders but works on the server model.
/// </summary>
public static class UiTreeAssertions
{
    public static void ShouldContainText(this UiWidgetTree tree, string expectedText)
    {
        if (ContainsTextRecursive(tree, expectedText))
            return;

        throw new Xunit.Sdk.XunitException(
            $"Expected tree to contain text '{expectedText}', but it did not.\nTree: {DumpTree(tree)}");
    }

    public static void ShouldHaveNodeOfType(this UiWidgetTree tree, string nodeType)
    {
        if (FindNode(tree, nodeType) != null)
            return;

        throw new Xunit.Sdk.XunitException($"Expected a node of type '{nodeType}'. Tree:\n{DumpTree(tree)}");
    }

    public static void ShouldHaveButtonWithLabel(this UiWidgetTree tree, string label)
    {
        var node = FindNode(tree, DigitalBrain.Core.Ui.Button);
        if (node != null && node.Props.TryGetValue("label", out var l) && l?.ToString() == label)
            return;

        // Also check common aliases used in emission
        if (FindByProp(tree, "label", label) != null)
            return;

        throw new Xunit.Sdk.XunitException($"No button with label '{label}' found.");
    }

    private static bool ContainsTextRecursive(UiWidgetTree node, string text)
    {
        if (node.Props.TryGetValue("text", out var t) && t?.ToString()?.Contains(text) == true)
            return true;

        if (node.Children != null)
            return node.Children.Any(c => ContainsTextRecursive(c, text));

        return false;
    }

    private static UiWidgetTree? FindNode(UiWidgetTree node, string type)
    {
        if (string.Equals(node.Type, type, StringComparison.OrdinalIgnoreCase))
            return node;

        return node.Children?.Select(c => FindNode(c, type)).FirstOrDefault(c => c != null);
    }

    private static UiWidgetTree? FindByProp(UiWidgetTree node, string propName, string value)
    {
        if (node.Props.TryGetValue(propName, out var v) && v?.ToString() == value)
            return node;

        return node.Children?.Select(c => FindByProp(c, propName, value)).FirstOrDefault(c => c != null);
    }

    private static string DumpTree(UiWidgetTree node, int indent = 0)
    {
        var pad = new string(' ', indent * 2);
        var line = $"{pad}{node.Type}";
        if (node.Props.Count > 0)
            line += " " + string.Join(", ", node.Props.Select(kv => $"{kv.Key}={kv.Value}"));

        if (node.Children?.Any() == true)
            line += "\n" + string.Join("\n", node.Children.Select(c => DumpTree(c, indent + 1)));

        return line;
    }
}