using DigitalBrain.Abstractions;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Core;

// The two explicit fake selections: Testing mode, or DigitalBrain:Fakes:Enabled. Development
// mode alone never selects a fake.
public static class DigitalBrainFakes
{
    public static bool Enabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.Equals(configuration[DigitalBrainNames.Mode], DigitalBrainNames.TestingMode, StringComparison.Ordinal))
        {
            return true;
        }

        var fakes = configuration[DigitalBrainNames.Fakes];
        return string.Equals(fakes, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fakes, "1", StringComparison.OrdinalIgnoreCase);
    }
}
