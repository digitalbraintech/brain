using System.Reflection;
using DigitalBrain.Mcp.Tools;
using ModelContextProtocol.Server;

namespace DigitalBrain.Tests.Mcp;

public class McpTransportSplitTests
{
    private static string[] ToolNames<T>() =>
        typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name!)
            .ToArray();

    [Fact]
    public void ReadTools_Expose_Only_ReadOnly_Tool_Names()
    {
        var read = ToolNames<DigitalBrainReadTools>();
        Assert.Equal(
            new[] { "get_timeline", "get_workbench_surfaces", "list_marketplace", "ping_digitalbrain" }.OrderBy(n => n),
            read.OrderBy(n => n));
    }

    [Fact]
    public void MutationTool_Names_Never_Leak_Into_The_Read_Surface()
    {
        var read = ToolNames<DigitalBrainReadTools>().ToHashSet();
        var mutation = ToolNames<DigitalBrainMutationTools>();

        Assert.Contains("publish_to_marketplace", mutation);
        Assert.Contains("install_from_marketplace", mutation);
        Assert.Contains("run_code_foundry", mutation);
        Assert.All(mutation, name => Assert.DoesNotContain(name, read));
    }
}
