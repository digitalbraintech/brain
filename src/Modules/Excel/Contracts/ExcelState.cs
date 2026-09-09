namespace DigitalBrain.Excel;

[GenerateSerializer]
[Alias("excel.workbook-state")]
public sealed record ExcelState(
    [property: Id(0)] string Title,
    [property: Id(1)] string SheetName,
    [property: Id(2)] IReadOnlyList<string> Columns,
    [property: Id(3)] IReadOnlyList<ExcelRow> Rows);

[GenerateSerializer]
[Alias("excel.row")]
public sealed record ExcelRow([property: Id(0)] IReadOnlyList<string> Cells);
