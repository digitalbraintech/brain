using DigitalBrain.Core;
using DigitalBrain.Developer;
using DigitalBrain.Windows;

namespace DigitalBrain.Tests.Sdk;

public class SdkContractsMetadataTests
{
    [Fact]
    public void Every_Sdk_Contract_Exposes_Static_Virtual_Metadata()
    {
        Assert.Equal("Git", NeuronAgentMetadata.ReadFrom<IGitNeuron>().DisplayName);
        Assert.Equal("Shell", NeuronAgentMetadata.ReadFrom<IShellNeuron>().DisplayName);
        Assert.Equal("FileSystem", NeuronAgentMetadata.ReadFrom<IFileSystemNeuron>().DisplayName);
        Assert.Equal("DotNet", NeuronAgentMetadata.ReadFrom<IDotNetNeuron>().DisplayName);
        Assert.Equal("NuGet", NeuronAgentMetadata.ReadFrom<INuGetNeuron>().DisplayName);
        Assert.Equal("Winget", NeuronAgentMetadata.ReadFrom<IWingetNeuron>().DisplayName);
        Assert.Equal("Roslyn", NeuronAgentMetadata.ReadFrom<IRoslynNeuron>().DisplayName);

        Assert.Contains("shell", NeuronAgentMetadata.ReadFrom<IShellNeuron>().Capabilities);
        Assert.Contains("winget", NeuronAgentMetadata.ReadFrom<IWingetNeuron>().Capabilities);
        Assert.False(string.IsNullOrWhiteSpace(NeuronAgentMetadata.ReadFrom<IRoslynNeuron>().Instructions));
    }
}

