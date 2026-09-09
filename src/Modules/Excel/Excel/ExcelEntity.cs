using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Excel;

[GrainType("excel")]
internal sealed class ExcelEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ExcelState> state)
    : Entity<ExcelState>(state), IExcel
{
    internal const int MaxColumns = 32;
    internal const int MaxRows = 64;

    public async Task Load(ExcelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        await SaveAsync(Normalize(state));
    }

    public async Task SetCell(int row, int column, string value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, MaxRows);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, MaxColumns);

        var current = State ?? new ExcelState("Sheet", "Sheet1", [], []);
        var columns = current.Columns.ToList();
        while (columns.Count <= column)
        {
            columns.Add(ColumnLetter(columns.Count));
        }

        var rows = current.Rows.Select(static r => r.Cells.ToList()).ToList();
        while (rows.Count <= row)
        {
            rows.Add([]);
        }

        foreach (var cells in rows)
        {
            while (cells.Count < columns.Count)
            {
                cells.Add("");
            }
        }

        rows[row][column] = value ?? "";
        await SaveAsync(new ExcelState(
            current.Title,
            current.SheetName,
            columns,
            rows.Select(static cells => new ExcelRow(cells)).ToArray()));
    }

    internal static ExcelState Normalize(ExcelState state)
    {
        var title = string.IsNullOrWhiteSpace(state.Title) ? "Sheet" : state.Title.Trim();
        var sheetName = string.IsNullOrWhiteSpace(state.SheetName) ? "Sheet1" : state.SheetName.Trim();
        var columns = (state.Columns ?? [])
            .Select(static column => string.IsNullOrWhiteSpace(column) ? "" : column.Trim())
            .Take(MaxColumns)
            .ToList();
        if (columns.Count == 0)
        {
            columns.Add("A");
        }

        var rows = (state.Rows ?? [])
            .Take(MaxRows)
            .Select(row =>
            {
                var cells = (row.Cells ?? []).Take(columns.Count).Select(static cell => cell ?? "").ToList();
                while (cells.Count < columns.Count)
                {
                    cells.Add("");
                }

                return new ExcelRow(cells);
            })
            .ToArray();

        return new ExcelState(title, sheetName, columns, rows);
    }

    internal static string ColumnLetter(int index)
    {
        var n = index;
        var letters = new Stack<char>();
        do
        {
            letters.Push((char)('A' + (n % 26)));
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return new string([.. letters]);
    }
}
