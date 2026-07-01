using DigitalBrain.Core;
using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Tests.Sdk;

public class SdkMetadataTests
{
    [Fact]
    public void NeuronAgentMetadata_Reads_Git_Contract_Without_Reflection()
    {
        // Compiler-resolved static-virtual members — no reflection involved.
        var md = NeuronAgentMetadata.ReadFrom<IGitNeuron>();

        Assert.Equal("Git", md.DisplayName);
        Assert.Contains("git", md.Capabilities);
        Assert.Contains("commit", md.Capabilities);
        Assert.False(string.IsNullOrWhiteSpace(md.Description));
        Assert.False(string.IsNullOrWhiteSpace(md.Instructions));
    }
}

