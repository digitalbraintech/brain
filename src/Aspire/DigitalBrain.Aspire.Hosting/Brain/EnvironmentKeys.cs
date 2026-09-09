namespace DigitalBrain.Aspire.Hosting;

// "DigitalBrain:Google:Gmail:OAuth" + "ClientId" -> DigitalBrain__Google__Gmail__OAuth__ClientId,
// the same key the module reads back through IConfiguration.
public static class EnvironmentKeys
{
    public static string For(string configurationRoot, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"{configurationRoot}:{name}".Replace(":", "__", StringComparison.Ordinal);
    }
}
