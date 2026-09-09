using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.Excel;

[Alias("excel.workbook")]
public interface IExcel : IEntity<ExcelState>
{
    [Alias(nameof(Load))]
    Task Load(ExcelState state);

    [Alias(nameof(SetCell))]
    Task SetCell(int row, int column, string value);
}
