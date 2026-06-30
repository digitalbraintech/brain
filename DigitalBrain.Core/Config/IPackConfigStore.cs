namespace DigitalBrain.Core.Config;

public interface IPackConfigStore
{
    Task SetAsync(string scope, string pack, IReadOnlyDictionary<string, string> values);
    Task<IReadOnlyDictionary<string, string>> GetAsync(string scope, string pack);
}
