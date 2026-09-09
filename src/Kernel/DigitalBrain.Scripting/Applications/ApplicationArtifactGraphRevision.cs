using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Scripting.Applications;

internal static class ApplicationArtifactGraphRevision
{
    internal static string Compute(FileApplicationArtifact artifact)
    {
        var fingerprint = new StringBuilder(artifact.RevisionId);
        foreach (var child in (artifact.Children ?? new Dictionary<string, FileApplicationArtifact>())
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            fingerprint.Append('\n').Append(child.Key).Append('=').Append(child.Value.ApplicationRevision);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
    }
}
