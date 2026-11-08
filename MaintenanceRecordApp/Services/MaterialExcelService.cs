using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MaintenanceRecordApp.Models;

namespace MaintenanceRecordApp.Services;

public sealed class MaterialImportResult
{
    public required IReadOnlyList<MaterialCatalogItem> Materials { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public static partial class MaterialExcelService
{
    public static MaterialImportResult ReadMaterials(string path)
    {
        using var workbook = new XLWorkbook(path);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Excel 文件中没有工作表。");
        var rows = worksheet.RowsUsed().Take(40).ToList();

        var headerRowIndex = -1;
        var columns = new ColumnMap();
        for (var index = 0; index < rows.Count; index++)
        {
            var map = BuildColumnMap(rows[index]);
            if (map.Name >= 0)
            {
                headerRowIndex = index;
                columns = map;
                break;
            }
        }

        var usingFallback = headerRowIndex < 0;
        if (usingFallback)
        {
            headerRowIndex = -1;
            columns = new ColumnMap { Name = 0, Specification = 1, Unit = 2, Price = 3 };
        }

        var result = new List<MaterialCatalogItem>();
        var warnings = new List<string>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var rowIndex = headerRowIndex + 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var name = ReadCell(row, columns.Name);
            if (string.IsNullOrWhiteSpace(name) ||
                Normalize(name) is "材料名称" or "名称" or "序号")
            {
                continue;
            }

            var specification = ReadCell(row, columns.Specification);
            var unit = ReadCell(row, columns.Unit);
            var priceText = ReadCell(row, columns.Price);
            var price = ParsePrice(priceText);
            if (!string.IsNullOrWhiteSpace(priceText) && !price.HasValue)
            {
                warnings.Add($"第 {row.RowNumber()} 行价格“{priceText}”无法识别，已留空。");
            }

            var key = $"{name}\u001f{specification}\u001f{unit}";
            if (!keys.Add(key))
            {
                continue;
            }

            result.Add(new MaterialCatalogItem
            {
                Name = name.Trim(),
                Specification = specification.Trim(),
                Unit = unit.Trim(),
                Price = price,
            });
        }

        if (usingFallback && result.Count == 0)
        {
            throw new InvalidOperationException(
                "未找到“材料名称”列。请确认第一张工作表包含材料名称、规格型号、单位和价格列。");
        }

        return new MaterialImportResult
        {
            Materials = result,
            Warnings = warnings,
        };
    }

    public static void CreateImportTemplate(string path)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("材料库");
        sheet.Cell("A1").Value = "材料名称";
        sheet.Cell("B1").Value = "规格型号";
        sheet.Cell("C1").Value = "单位";
        sheet.Cell("D1").Value = "价格";
        sheet.Cell("A2").Value = "示例材料";
        sheet.Cell("B2").Value = "DN20";
        sheet.Cell("C2").Value = "个";
        sheet.Cell("D2").Value = 60;
        StyleHeader(sheet.Range("A1:D1"));
        sheet.Range("A1:D2").Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        sheet.Range("A1:D2").Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        sheet.Columns().AdjustToContents();
        sheet.Column(4).Width = Math.Max(sheet.Column(4).Width, 12);
        workbook.SaveAs(path);
    }

    public static void ExportMaterials(string path, IEnumerable<MaterialCatalogItem> materials)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("材料库");
        var headers = new[] { "材料名称", "规格型号", "单位", "价格" };
        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        var row = 2;
        foreach (var material in materials.OrderBy(item => item.Name))
        {
            sheet.Cell(row, 1).Value = material.Name;
            sheet.Cell(row, 2).Value = material.Specification;
            sheet.Cell(row, 3).Value = material.Unit;
            if (material.Price.HasValue)
            {
                sheet.Cell(row, 4).Value = material.Price.Value;
            }
            row++;
        }

        StyleHeader(sheet.Range(1, 1, 1, 4));
        if (row > 2)
        {
            sheet.Range(2, 4, row - 1, 4).Style.NumberFormat.Format = "0.00";
            sheet.Range(1, 1, row - 1, 4).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            sheet.Range(1, 1, row - 1, 4).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        }
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    private static ColumnMap BuildColumnMap(IXLRow row)
    {
        var map = new ColumnMap();
        foreach (var cell in row.CellsUsed())
        {
            var header = Normalize(cell.GetString());
            var column = cell.Address.ColumnNumber - 1;
            if (map.Name < 0 && (header.Contains("材料名称") || header == "名称" || header == "物资名称"))
            {
                map.Name = column;
            }
            else if (map.Specification < 0 &&
                     (header.Contains("规格型号") || header == "规格" || header == "型号"))
            {
                map.Specification = column;
            }
            else if (map.Unit < 0 && header.Contains("单位"))
            {
                map.Unit = column;
            }
            else if (map.Price < 0 &&
                     (header.Contains("材料价格") || header.Contains("单价") ||
                      header == "价格" || header.Contains("费用")))
            {
                map.Price = column;
            }
        }
        return map;
    }

    private static string ReadCell(IXLRow row, int zeroBasedColumn)
    {
        if (zeroBasedColumn < 0)
        {
            return string.Empty;
        }
        return row.Cell(zeroBasedColumn + 1).GetFormattedString().Trim();
    }

    private static decimal? ParsePrice(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var normalized = value
            .Replace("¥", string.Empty)
            .Replace("￥", string.Empty)
            .Replace("元", string.Empty)
            .Replace(",", string.Empty)
            .Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out var number) ||
               decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static string Normalize(string value) =>
        WhitespaceRegex.Replace(value ?? string.Empty, string.Empty)
            .Replace("（", string.Empty)
            .Replace("）", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Trim();

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontName = "Microsoft YaHei";
        range.Style.Font.FontColor = XLColor.FromHtml("#17332D");
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDF2EC");
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        range.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
    }

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private sealed class ColumnMap
    {
        public int Name { get; set; } = -1;
        public int Specification { get; set; } = -1;
        public int Unit { get; set; } = -1;
        public int Price { get; set; } = -1;
    }
}
