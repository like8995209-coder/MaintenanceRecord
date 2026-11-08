using System.IO;
using ClosedXML.Excel;
using MaintenanceRecordApp.Models;

namespace MaintenanceRecordApp.Services;

public static class LedgerExcelService
{
    private static readonly string[] Headers =
    [
        "编号", "外业工单编号", "单据来源", "接单时间", "位置",
        "事件与联系方式", "维修方式", "维修人", "完工时间", "备注",
        "是否杂工", "杂工工时（小时）", "劳务公司", "是否机械", "机械工时（小时）",
        "材料名称", "是否还原", "机械费用", "杂工费用", "材料费用",
        "还原费用", "维修成本合计", "维修前图", "维修后图", "机械使用图", "还原路面图",
    ];

    public static void Export(string path, IReadOnlyList<MaintenanceRecord> records)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("维修台账");
        var imageSheet = workbook.Worksheets.Add("图片清单");
        const int columnCount = 26;

        sheet.Range(1, 1, 1, columnCount).Merge();
        sheet.Cell(1, 1).Value = "管网部维修台账";
        sheet.Cell(1, 1).Style.Font.FontName = "Microsoft YaHei";
        sheet.Cell(1, 1).Style.Font.FontSize = 18;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 32;

        sheet.Range(2, 1, 2, 10).Merge();
        sheet.Cell(2, 1).Value = "维修事件";
        sheet.Range(2, 11, 2, 17).Merge();
        sheet.Cell(2, 11).Value = "维修过程";
        sheet.Range(2, 18, 2, 22).Merge();
        sheet.Cell(2, 18).Value = "维修费用（元）";
        sheet.Range(2, 23, 2, 26).Merge();
        sheet.Cell(2, 23).Value = "维修图片";
        StyleGroupHeader(sheet.Range(2, 1, 2, columnCount));
        sheet.Row(2).Height = 21;

        for (var column = 1; column <= columnCount; column++)
        {
            sheet.Cell(3, column).Value = Headers[column - 1];
        }
        StyleTableHeader(sheet.Range(3, 1, 3, columnCount));
        sheet.Row(3).Height = 34;

        var row = 4;
        var imageRow = 2;
        foreach (var record in records.OrderBy(item => item.OrderNumber))
        {
            var values = new object?[]
            {
                record.OrderNumber,
                record.ExternalOrderNumber,
                record.Source,
                record.WorkDateTimeText,
                record.Location,
                string.Join("，", new[] { record.RepairReason, record.Contact }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
                record.RepairMethod,
                record.RepairPerson,
                record.CompletionDateTimeText,
                record.Notes,
                record.HasOddJob ? "是" : "否",
                record.OddJobTime,
                record.LaborCompany,
                record.HasMachinery ? "是" : "否",
                record.MachineryTime,
                FormatMaterials(record),
                record.Restored ? "是" : "否",
                record.MachineryCost,
                record.OddJobCost,
                record.MaterialCost,
                record.RestorationCost,
                record.TotalCost,
                FormatPhotoNames(record.BeforePhotos),
                FormatPhotoNames(record.AfterPhotos),
                FormatPhotoNames(record.MachineryPhotos),
                FormatPhotoNames(record.RestorationPhotos),
            };

            for (var column = 1; column <= columnCount; column++)
            {
                var value = values[column - 1];
                if (value is decimal number)
                {
                    sheet.Cell(row, column).Value = number;
                }
                else if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    sheet.Cell(row, column).Value = text;
                }
            }
            SetPhotoHyperlink(sheet.Cell(row, 23), record.BeforePhotos);
            SetPhotoHyperlink(sheet.Cell(row, 24), record.AfterPhotos);
            SetPhotoHyperlink(sheet.Cell(row, 25), record.MachineryPhotos);
            SetPhotoHyperlink(sheet.Cell(row, 26), record.RestorationPhotos);

            imageRow = AddPhotoRows(
                imageSheet,
                imageRow,
                record.OrderNumber,
                "维修前",
                record.BeforePhotos);
            imageRow = AddPhotoRows(
                imageSheet,
                imageRow,
                record.OrderNumber,
                "施工现场",
                record.DuringPhotos);
            imageRow = AddPhotoRows(
                imageSheet,
                imageRow,
                record.OrderNumber,
                "维修后",
                record.AfterPhotos);
            imageRow = AddPhotoRows(
                imageSheet,
                imageRow,
                record.OrderNumber,
                "机械使用",
                record.MachineryPhotos);
            imageRow = AddPhotoRows(
                imageSheet,
                imageRow,
                record.OrderNumber,
                "还原路面",
                record.RestorationPhotos);
            row++;
        }

        var lastRow = Math.Max(3, row - 1);
        var body = sheet.Range(4, 1, lastRow, columnCount);
        body.Style.Font.FontName = "Microsoft YaHei";
        body.Style.Font.FontSize = 9;
        body.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        body.Style.Alignment.WrapText = true;
        body.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        body.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);

        if (lastRow >= 4)
        {
            sheet.Range(4, 18, lastRow, 22).Style.NumberFormat.Format = "#,##0.00";
            for (var dataRow = 4; dataRow <= lastRow; dataRow++)
            {
                sheet.Row(dataRow).Height = 34;
            }
        }

        StyleTableHeader(sheet.Range(3, 1, 3, columnCount));
        sheet.Columns(1, 26).Width = 14;
        sheet.Column(1).Width = 24;
        sheet.Column(2).Width = 18;
        sheet.Column(3).Width = 12;
        sheet.Column(4).Width = 20;
        sheet.Column(6).Width = 30;
        sheet.Column(7).Width = 16;
        sheet.Column(8).Width = 12;
        sheet.Column(9).Width = 20;
        sheet.Column(10).Width = 24;
        sheet.Column(16).Width = 22;
        sheet.Column(23).Width = 18;
        sheet.Column(24).Width = 18;
        sheet.Column(25).Width = 18;
        sheet.Column(26).Width = 18;
        sheet.SheetView.FreezeRows(3);
        sheet.SheetView.FreezeColumns(1);
        sheet.Range(3, 1, lastRow, columnCount).SetAutoFilter();
        sheet.ShowGridLines = false;
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;

        imageSheet.Cell(1, 1).Value = "工单编号";
        imageSheet.Cell(1, 2).Value = "照片类别";
        imageSheet.Cell(1, 3).Value = "文件名";
        imageSheet.Cell(1, 4).Value = "打开图片";
        StyleTableHeader(imageSheet.Range(1, 1, 1, 4));
        if (imageRow > 2)
        {
            var imageBody = imageSheet.Range(2, 1, imageRow - 1, 4);
            imageBody.Style.Font.FontName = "Microsoft YaHei";
            imageBody.Style.Font.FontSize = 10;
            imageBody.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            imageBody.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            imageBody.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            imageBody.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            imageSheet.Column(1).Width = 28;
            imageSheet.Column(2).Width = 16;
            imageSheet.Column(3).Width = 38;
            imageSheet.Column(4).Width = 20;
            imageSheet.SheetView.FreezeRows(1);
            imageSheet.ShowGridLines = false;
        }

        workbook.SaveAs(path);
    }

    private static int AddPhotoRows(
        IXLWorksheet sheet,
        int row,
        string orderNumber,
        string category,
        IEnumerable<string> photoPaths)
    {
        foreach (var path in photoPaths.Where(File.Exists))
        {
            sheet.Cell(row, 1).Value = orderNumber;
            sheet.Cell(row, 2).Value = category;
            sheet.Cell(row, 3).Value = Path.GetFileName(path);
            var linkCell = sheet.Cell(row, 4);
            linkCell.Value = "打开图片";
            linkCell.SetHyperlink(
                new XLHyperlink(new Uri(Path.GetFullPath(path)), Path.GetFullPath(path)));
            StyleHyperlink(linkCell);
            row++;
        }
        return row;
    }

    private static void SetPhotoHyperlink(IXLCell cell, IEnumerable<string> photoPaths)
    {
        var firstPath = photoPaths.FirstOrDefault(File.Exists);
        if (firstPath is null)
        {
            return;
        }

        cell.SetHyperlink(
            new XLHyperlink(
                new Uri(Path.GetFullPath(firstPath)),
                "点击打开第一张图片；完整图片列表见“图片清单”工作表"));
        StyleHyperlink(cell);
    }

    private static void StyleHyperlink(IXLCell cell)
    {
        cell.Style.Font.FontColor = XLColor.FromHtml("#0563C1");
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
    }

    private static string FormatMaterials(MaintenanceRecord record) =>
        string.Join(
            "；",
            record.Materials.Select(item =>
            {
                var specification = string.IsNullOrWhiteSpace(item.Specification)
                    ? string.Empty
                    : $"/{item.Specification}";
                var unit = string.IsNullOrWhiteSpace(item.Unit) ? string.Empty : item.Unit;
                return $"{item.Name}{specification} ×{item.Quantity:0.##}{unit}";
            }));

    private static string FormatPhotoNames(IEnumerable<string> paths) =>
        string.Join(
            Environment.NewLine,
            paths.Select(path => Path.GetFileName(path)));

    private static void StyleGroupHeader(IXLRange range)
    {
        range.Style.Font.FontName = "Microsoft YaHei";
        range.Style.Font.FontSize = 9;
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.FromHtml("#17332D");
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#E7EFEC");
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
    }

    private static void StyleTableHeader(IXLRange range)
    {
        range.Style.Font.FontName = "Microsoft YaHei";
        range.Style.Font.FontSize = 9;
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDF2EC");
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        range.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
    }
}
