using DigitalBrain.Abstractions.Scripting;
using Xunit;

namespace DigitalBrain.AI.Tests;

public sealed class ApplicationContractsTests
{
    [Fact]
    public void Different_types_cannot_claim_the_same_wire_contract()
    {
        var name = $"tests.contract-{Guid.NewGuid():N}";
        ApplicationContracts.RegisterJson<FirstContract>(name, 1);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ApplicationContracts.RegisterJson<SecondContract>(name, 1));

        Assert.Contains("already registered", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FirstContract).FullName!, error.Message, StringComparison.Ordinal);
    }

    private sealed record FirstContract(string Value);
    private sealed record SecondContract(string Value);
}

