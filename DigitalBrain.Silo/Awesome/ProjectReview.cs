using System.Text;

namespace DigitalBrain.Silo;

public sealed record ProjectReviewOutcome(string Summary, string Report, int FileCount, int TodoCount, bool Truncated);

// Real, kernel-local software-engineering review. Ported near-verbatim from final's ProjectReview.Analyze
// (pure C#, no DSL): enumerate *.cs (skipping bin/obj), count TODOs, cap at 100 files / 1 MB, honest
// missing-path error, Markdown report.
public static class ProjectReview
{
    private const int MaxFiles = 100;
    private const long MaxTotalBytes = 1_000_000;

    public static ProjectReviewOutcome Analyze(string path)
    {
        if (!Directory.Exists(path))
        {
            var missing = $"Path '{path}' does not exist on the kernel machine.";
            return new ProjectReviewOutcome(
                missing,
                $"# Review failed\n\n{missing}\n\nThe kernel resolves paths locally — the path must be valid where the kernel runs, not where the client runs.",
                0, 0, false);
        }

        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Take(MaxFiles + 1)
            .ToList();

        var truncated = files.Count > MaxFiles;
        if (truncated) files.RemoveAt(MaxFiles);

        long totalBytes = 0;
        var totalLines = 0;
        var todoCount = 0;
        var flagged = new List<(string File, int Todos, int Lines)>();
        var largest = new List<(string File, int Lines)>();

        foreach (var file in files)
        {
            var length = new FileInfo(file).Length;
            if (totalBytes + length > MaxTotalBytes)
            {
                truncated = true;
                break;
            }
            totalBytes += length;
            var lines = File.ReadAllLines(file);
            var todos = lines.Count(l => l.Contains("TODO", StringComparison.Ordinal));
            totalLines += lines.Length;
            todoCount += todos;
            var rel = Path.GetRelativePath(path, file);
            if (todos > 0) flagged.Add((rel, todos, lines.Length));
            largest.Add((rel, lines.Length));
        }

        var analyzed = largest.Count;
        var summary = $"Reviewed {analyzed} C# files ({totalLines} lines) at {path}. TODOs: {todoCount}." +
            (truncated ? $" Truncated at {MaxFiles} files / {MaxTotalBytes / 1000} KB." : "");

        var report = new StringBuilder()
            .AppendLine($"# Review: {path}")
            .AppendLine()
            .AppendLine($"**{analyzed} files**, **{totalLines} lines**, **{todoCount} TODOs**" + (truncated ? " *(truncated by caps)*" : ""))
            .AppendLine();

        if (flagged.Count > 0)
        {
            report.AppendLine("## Files with TODOs").AppendLine();
            foreach (var (file, todos, lines) in flagged.OrderByDescending(f => f.Todos).Take(10))
                report.AppendLine($"- `{file}` — {todos} TODO(s), {lines} lines");
            report.AppendLine();
        }

        report.AppendLine("## Largest files").AppendLine();
        foreach (var (file, lines) in largest.OrderByDescending(f => f.Lines).Take(5))
            report.AppendLine($"- `{file}` — {lines} lines");

        report.AppendLine().AppendLine(todoCount > 0
            ? "Suggestion: address the flagged TODOs, largest files first."
            : "Suggestion: no TODOs found; consider expanding test coverage.");

        return new ProjectReviewOutcome(summary, report.ToString(), analyzed, todoCount, truncated);
    }
}
