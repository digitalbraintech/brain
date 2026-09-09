using System.ComponentModel;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Excel;

internal sealed class ExcelToolSource(IGrainFactory grains) : IAgentToolSource
{
    public ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.RequireActive();
        return ValueTask.FromResult<IReadOnlyList<AITool>>(CreateTools(context.Owner));
    }

    private IReadOnlyList<AIFunction> CreateTools(OwnerId owner)
    {
        Task<string> ShowSpreadsheet(
            [Description("The current chat's name, exactly as stated in the conversation context")] string chatName,
            [Description("Spreadsheet title")] string title,
            [Description("Sheet tab name")] string sheetName,
            [Description("Column headers, comma-separated")] string headers,
            [Description("Data rows, one row per line, cells comma-separated")] string rows,
            CancellationToken cancellationToken)
            => ShowSpreadsheetAsync(owner, chatName, title, sheetName, headers, rows, cancellationToken);

        return
        [
            AIFunctionFactory.Create(ShowSpreadsheet, new AIFunctionFactoryOptions
            {
                Name = "show_spreadsheet",
                Description = "Show a live spreadsheet card in the chat. Use it when the owner asks to see "
                    + "an Excel file, a sheet, a table of cells, or yesterday's spreadsheet.",
            }),
        ];
    }

    private static string? OwnerGuardError(OwnerId owner, string chatName)
    {
        var ownerPrefix = $"{owner.Value}/";
        return chatName.StartsWith(ownerPrefix, StringComparison.Ordinal)
            ? null
            : $"chatName must be a chat key of this owner (starting with '{ownerPrefix}').";
    }

    private async Task<string> ShowSpreadsheetAsync(
        OwnerId owner,
        string chatName,
        string title,
        string sheetName,
        string headers,
        string rows,
        CancellationToken cancellationToken)
    {
        try
        {
            if (OwnerGuardError(owner, chatName) is { } ownerError)
            {
                return ownerError;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return "title must not be blank.";
            }

            var columns = SplitCsv(headers);
            if (columns.Count == 0)
            {
                return "headers must contain at least one column.";
            }

            var parsedRows = SplitRows(rows, columns.Count);
            var trimmedTitle = title.Trim();
            var name = $"sheet-{Guid.NewGuid():N}"[..14];
            var instance = ExcelKitNames.Sibling(chatName, name);

            await grains.GetGrain<IExcel>(instance)
                .Load(new ExcelState(trimmedTitle, sheetName ?? "Sheet1", columns, parsedRows));
            await grains.GetGrain<IChat>(chatName)
                .HandleAsync(new KitCardOffer(KitCardKinds.Spreadsheet, name, trimmedTitle), cancellationToken);

            return $"Spreadsheet '{trimmedTitle}' is now showing in the chat as card '{name}'.";
        }
        catch (Exception ex)
        {
            return $"show_spreadsheet failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static List<string> SplitCsv(string? line)
        => string.IsNullOrWhiteSpace(line)
            ? []
            : [.. line.Split(',').Select(static part => part.Trim()).Where(static part => part.Length > 0)];

    private static ExcelRow[] SplitRows(string? rows, int columnCount)
    {
        if (string.IsNullOrWhiteSpace(rows))
        {
            return [];
        }

        return [.. rows
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Take(ExcelEntity.MaxRows)
            .Select(line =>
            {
                var cells = line.Split(',').Select(static part => part.Trim()).ToList();
                while (cells.Count < columnCount)
                {
                    cells.Add("");
                }

                return new ExcelRow(cells.Take(columnCount).ToArray());
            })];
    }
}
