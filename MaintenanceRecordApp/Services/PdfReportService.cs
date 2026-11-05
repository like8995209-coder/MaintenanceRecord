using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using MaintenanceRecordApp.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MaintenanceRecordApp.Services;

public static class PdfReportService
{
    private const double PageWidthMm = 297;
    private const double PageHeightMm = 210;
    private const double Left = 24;
    private const double ContentWidth = 794;
    private const double ColumnGap = 14;
    private const double ColumnWidth = (ContentWidth - ColumnGap) / 2;
    private const double RightX = Left + ColumnWidth + ColumnGap;

    private static readonly XColor Ink = XColor.FromArgb(27, 37, 34);
    private static readonly XColor Muted = XColor.FromArgb(92, 107, 101);
    private static readonly XColor Accent = XColor.FromArgb(0, 102, 87);
    private static readonly XColor Pale = XColor.FromArgb(240, 247, 244);
    private static readonly XColor Line = XColor.FromArgb(166, 181, 174);
    private static readonly XColor White = XColor.FromArgb(255, 255, 255);

    public static void Generate(string outputPath, MaintenanceRecord record)
    {
        PdfFontSupport.EnsureInitialized();
        if (!record.CanGenerate)
        {
            throw new InvalidOperationException(record.CompletionSummary);
        }
        if (record.Materials.Count > 12)
        {
            throw new InvalidOperationException("单张横版 A4 最多容纳 12 项材料，请拆分记录后导出。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);
        using var document = new PdfDocument();
        document.Info.Title = $"维修记录 {record.OrderNumber}";
        document.Info.Author = "且萌且机制";
        document.Info.Subject = "维修记录";

        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(PageWidthMm);
        page.Height = XUnit.FromMillimeter(PageHeightMm);
        using var graphics = XGraphics.FromPdfPage(page);
        DrawHeader(graphics, record);
        DrawBasicInfo(graphics, record);
        DrawMaterials(graphics, record);
        DrawCostDetails(graphics, record);
        DrawTotalAndNotes(graphics, record);
        DrawPhotos(graphics, record);
        DrawAcceptance(graphics, record);
        DrawFooter(graphics);
        document.Save(outputPath);
    }

    private static void DrawHeader(XGraphics graphics, MaintenanceRecord record)
    {
        graphics.DrawString(
            "维修记录",
            CreateFont(22, XFontStyleEx.Bold),
            new XSolidBrush(Ink),
            new XRect(Left, 24, ContentWidth, 34),
            XStringFormats.Center);
        graphics.DrawLine(new XPen(Line, 0.8), Left, 64, Left + ContentWidth, 64);
    }

    private static void DrawBasicInfo(XGraphics graphics, MaintenanceRecord record)
    {
        DrawSectionHeader(graphics, "基本信息", 70, Left, ColumnWidth);
        const double top = 90;
        const double width = ColumnWidth;
        var narrowRows = new[] { 15d, 15d, 15d, 15d };
        const double reasonRowHeight = 22;
        var rowTop = top;
        var pen = new XPen(Line, 0.55);
        var labelFont = CreateFont(7.1, XFontStyleEx.Bold);
        var valueFont = CreateFont(7.3, XFontStyleEx.Regular);
        var boundaries = new[] { 72d, 205d, 277d };

        var normalRows = new[]
        {
            new[] { "维修编号", record.OrderNumber, "接单时间", record.WorkDateTimeText },
            new[] { "维修人员", record.RepairPerson, "竣工时间", record.CompletionDateTimeText },
            new[] { "维修位置", record.Location, "联系方式", record.Contact },
            new[] { "单据来源", record.Source, "维修方式", record.RepairMethod },
        };

        for (var rowIndex = 0; rowIndex < normalRows.Length; rowIndex++)
        {
            var height = narrowRows[rowIndex];
            graphics.DrawRectangle(new XSolidBrush(White), Left, rowTop, width, height);
            graphics.DrawRectangle(pen, Left, rowTop, width, height);
            graphics.DrawLine(pen, Left + boundaries[0], rowTop, Left + boundaries[0], rowTop + height);
            graphics.DrawLine(pen, Left + boundaries[1], rowTop, Left + boundaries[1], rowTop + height);
            graphics.DrawLine(pen, Left + boundaries[2], rowTop, Left + boundaries[2], rowTop + height);
            DrawCellText(graphics, normalRows[rowIndex][0], labelFont, Left, rowTop, boundaries[0], height);
            DrawCellText(graphics, normalRows[rowIndex][1], valueFont, Left + boundaries[0], rowTop, boundaries[1] - boundaries[0], height);
            DrawCellText(graphics, normalRows[rowIndex][2], labelFont, Left + boundaries[1], rowTop, boundaries[2] - boundaries[1], height);
            DrawCellText(graphics, normalRows[rowIndex][3], valueFont, Left + boundaries[2], rowTop, width - boundaries[2], height);
            rowTop += height;
        }

        graphics.DrawRectangle(new XSolidBrush(White), Left, rowTop, width, reasonRowHeight);
        graphics.DrawRectangle(pen, Left, rowTop, width, reasonRowHeight);
        graphics.DrawLine(pen, Left + boundaries[0], rowTop, Left + boundaries[0], rowTop + reasonRowHeight);
        DrawCellText(graphics, "维修原因", labelFont, Left, rowTop, boundaries[0], reasonRowHeight);
        DrawCellText(
            graphics,
            string.IsNullOrWhiteSpace(record.RepairReason) ? "-" : record.RepairReason,
            valueFont,
            Left + boundaries[0],
            rowTop,
            width - boundaries[0],
            reasonRowHeight);
    }

    private static void DrawMaterials(XGraphics graphics, MaintenanceRecord record)
    {
        DrawSectionHeader(graphics, "材料清单", 178, Left, ColumnWidth);
        const double x = Left;
        const double y = 198;
        const double width = ColumnWidth;
        const double headerHeight = 20;
        const double tableBudget = 120;
        var rows = Math.Max(7, record.Materials.Count);
        var rowHeight = Math.Min(16, tableBudget / rows);
        var fontSize = Math.Max(6.6, Math.Min(8, rowHeight * 0.54));
        var pen = new XPen(Line, 0.55);
        var columns = new[] { 0.075, 0.23, 0.17, 0.085, 0.075, 0.115, 0.125, 0.125 };
        var headers = new[] { "序号", "材料名称", "规格型号", "数量", "单位", "单价", "小计", "备注" };
        var widths = columns.Select(value => value * width).ToArray();

        graphics.DrawRectangle(new XSolidBrush(Pale), x, y, width, headerHeight);
        graphics.DrawRectangle(pen, x, y, width, headerHeight);
        DrawCellsCentered(
            graphics,
            x,
            y,
            widths,
            headerHeight,
            headers,
            CreateFont(Math.Max(7, fontSize), XFontStyleEx.Bold),
            pen);

        for (var index = 0; index < rows; index++)
        {
            var material = index < record.Materials.Count ? record.Materials[index] : null;
            var values = material is null
                ? new[] { (index + 1).ToString(CultureInfo.InvariantCulture), "", "", "", "", "", "", "" }
                : new[]
                {
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    material.Name,
                    material.Specification,
                    material.Quantity.ToString("0.##", CultureInfo.CurrentCulture),
                    material.Unit,
                    material.UnitPrice?.ToString("0.00", CultureInfo.CurrentCulture) ?? "",
                    material.Subtotal.ToString("0.00", CultureInfo.CurrentCulture),
                    material.Note,
                };
            var rowY = y + headerHeight + index * rowHeight;
            graphics.DrawRectangle(new XSolidBrush(White), x, rowY, width, rowHeight);
            graphics.DrawRectangle(pen, x, rowY, width, rowHeight);
            DrawCellsCentered(
                graphics,
                x,
                rowY,
                widths,
                rowHeight,
                values,
                CreateFont(fontSize, XFontStyleEx.Regular),
                pen);
        }
    }

    private static void DrawCostDetails(XGraphics graphics, MaintenanceRecord record)
    {
        DrawSectionHeader(graphics, "费用清单", 344, Left, ColumnWidth);
        const double x = Left;
        const double y = 364;
        const double width = ColumnWidth;
        const double headerHeight = 20;
        const double rowHeight = 27;
        var pen = new XPen(Line, 0.55);
        var columns = new[] { 0.12, 0.34, 0.12, 0.13, 0.14, 0.15 };
        var widths = columns.Select(value => value * width).ToArray();
        var headers = new[] { "项目", "类型 / 劳务公司", "人数/台数", "时间/面积", "单价/综合价", "小计" };
        graphics.DrawRectangle(new XSolidBrush(Pale), x, y, width, headerHeight);
        graphics.DrawRectangle(pen, x, y, width, headerHeight);
        DrawCellsCentered(graphics, x, y, widths, headerHeight, headers, CreateFont(7.5, XFontStyleEx.Bold), pen);

        var rows = new[]
        {
            new[]
            {
                "杂工",
                Combine(record.OddJobType, record.LaborCompany),
                record.HasOddJob ? record.OddJobPeople.ToString(CultureInfo.InvariantCulture) : "",
                record.HasOddJob ? record.OddJobTime : "",
                record.HasOddJob ? record.OddJobRateText : "",
                Money(record.OddJobCost, record.HasOddJob),
            },
            new[]
            {
                "机械",
                record.MachineryType,
                record.HasMachinery ? "1" : "",
                record.HasMachinery ? record.MachineryTime : "",
                record.HasMachinery ? record.MachineryRateText : "",
                Money(record.MachineryCost, record.HasMachinery),
            },
            new[]
            {
                "还原",
                record.RestorationType,
                record.Restored ? record.RestorationAreaText : "",
                record.Restored ? "㎡" : "",
                record.Restored ? record.RestorationUnitPriceText : "",
                Money(record.RestorationCost, record.Restored),
            },
        };
        for (var index = 0; index < rows.Length; index++)
        {
            var rowY = y + headerHeight + index * rowHeight;
            graphics.DrawRectangle(new XSolidBrush(White), x, rowY, width, rowHeight);
            graphics.DrawRectangle(pen, x, rowY, width, rowHeight);
            DrawCellsCentered(
                graphics,
                x,
                rowY,
                widths,
                rowHeight,
                rows[index],
                CreateFont(7.4, XFontStyleEx.Regular),
                pen);
        }
    }

    private static void DrawTotalAndNotes(XGraphics graphics, MaintenanceRecord record)
    {
        const double x = Left;
        const double y = 468;
        const double width = ColumnWidth;
        graphics.DrawRectangle(new XSolidBrush(Pale), x, y, width, 33);
        graphics.DrawRectangle(new XPen(Line, 0.55), x, y, width, 33);
        graphics.DrawString("成本合计", CreateFont(8.2, XFontStyleEx.Bold), new XSolidBrush(Muted), x + 9, y + 12);
        graphics.DrawString(
            $"材料 ¥{record.MaterialCost:0.00} · 人工 ¥{record.OddJobCost:0.00} · 机械 ¥{record.MachineryCost:0.00} · 还原 ¥{record.RestorationCost:0.00}",
            CreateFont(7.1, XFontStyleEx.Regular),
            new XSolidBrush(Ink),
            x + 9,
            y + 27);
        graphics.DrawString(
            $"¥{record.TotalCost:0.00}",
            CreateFont(15, XFontStyleEx.Bold),
            new XSolidBrush(Accent),
            new XRect(x + 245, y + 5, width - 255, 22),
            XStringFormats.CenterRight);

        DrawSectionHeader(graphics, "备注", 505, Left, width);
        var notesRect = new XRect(x, 525, width, 27);
        graphics.DrawRectangle(new XSolidBrush(White), notesRect);
        graphics.DrawRectangle(new XPen(Line, 0.5), notesRect);
        DrawWrappedText(
            graphics,
            string.IsNullOrWhiteSpace(record.Notes) ? "无" : record.Notes,
            CreateFont(7.8, XFontStyleEx.Regular),
            new XRect(x + 6, 527, width - 12, 22),
            2,
            XStringFormats.TopLeft);
    }

    private static void DrawPhotos(XGraphics graphics, MaintenanceRecord record)
    {
        DrawSectionHeader(graphics, "现场施工图", 70, RightX, ColumnWidth);
        const double x = RightX;
        const double y = 90;
        const double width = ColumnWidth;
        const double bottom = 420;
        var rowHeight = (bottom - y) / 3d;
        var cellWidth = width / 2;
        var pen = new XPen(Line, 0.72);
        graphics.DrawRectangle(new XSolidBrush(White), x, y, width, bottom - y);
        graphics.DrawRectangle(pen, x, y, width, bottom - y);
        graphics.DrawLine(pen, x, y + rowHeight, x + width, y + rowHeight);
        graphics.DrawLine(pen, x, y + rowHeight * 2, x + width, y + rowHeight * 2);
        graphics.DrawLine(pen, x + cellWidth, y, x + cellWidth, y + rowHeight * 2);

        DrawPhotoCell(graphics, "维修前", record.BeforePhotos, x, y, cellWidth, rowHeight);
        DrawPhotoCell(graphics, "维修后", record.AfterPhotos, x + cellWidth, y, cellWidth, rowHeight);
        DrawPhotoCell(graphics, "机械使用", record.MachineryPhotos, x, y + rowHeight, cellWidth, rowHeight);
        DrawPhotoCell(graphics, "还原路面", record.RestorationPhotos, x + cellWidth, y + rowHeight, cellWidth, rowHeight);
        DrawPhotoCell(graphics, "施工现场", record.DuringPhotos, x, y + rowHeight * 2, width, rowHeight);
    }

    private static void DrawPhotoCell(
        XGraphics graphics,
        string label,
        IReadOnlyList<string> photos,
        double x,
        double y,
        double width,
        double height)
    {
        if (photos.FirstOrDefault() is { } path && File.Exists(path))
        {
            TryDrawImage(graphics, path, x + 2, y + 2, width - 4, height - 4);
        }
        else
        {
            graphics.DrawString(
                "暂无照片",
                CreateFont(7, XFontStyleEx.Regular),
                new XSolidBrush(Muted),
                new XRect(x, y, width, height),
                XStringFormats.Center);
        }

        var labelWidth = Math.Min(78, 18 + label.Length * 8);
        graphics.DrawRectangle(
            new XSolidBrush(XColor.FromArgb(205, 6, 64, 68)),
            x + 5,
            y + 5,
            labelWidth,
            16);
        graphics.DrawString(
            label,
            CreateFont(7.2, XFontStyleEx.Bold),
            new XSolidBrush(White),
            new XRect(x + 5, y + 5, labelWidth, 16),
            XStringFormats.Center);
    }

    private static void DrawAcceptance(XGraphics graphics, MaintenanceRecord record)
    {
        DrawSectionHeader(graphics, "验收", 426, RightX, ColumnWidth);
        const double x = RightX;
        const double y = 446;
        const double width = ColumnWidth;
        const double height = 105;
        var pen = new XPen(Line, 0.55);
        graphics.DrawRectangle(new XSolidBrush(Pale), x, y, width, height);
        graphics.DrawRectangle(pen, x, y, width, height);
        graphics.DrawLine(pen, x + width / 2, y, x + width / 2, y + height);
        graphics.DrawLine(pen, x, y + 63, x + width, y + 63);

        var labelFont = CreateFont(7.4, XFontStyleEx.Bold);
        var valueFont = CreateFont(8, XFontStyleEx.Regular);
        DrawCenteredPair(graphics, "验收结果", record.AcceptanceText, x, y + 5, width / 2, 29, labelFont, valueFont);
        DrawCenteredPair(graphics, "验收部门", record.ReviewDepartment, x + width / 2, y + 5, width / 2, 29, labelFont, valueFont);
        DrawCenteredPair(graphics, "制单人", record.PreparedBy, x, y + 34, width / 2, 29, labelFont, valueFont);
        DrawCenteredPair(graphics, "现场负责人", record.ResponsiblePerson, x + width / 2, y + 34, width / 2, 29, labelFont, valueFont);
        graphics.DrawString("验收人签字：", labelFont, new XSolidBrush(Muted), x + 10, y + 80);
        graphics.DrawLine(new XPen(Line, 0.5), x + 63, y + 85, x + width / 2 - 8, y + 85);
        graphics.DrawString("现场确认：", labelFont, new XSolidBrush(Muted), x + width / 2 + 10, y + 80);
        graphics.DrawLine(new XPen(Line, 0.5), x + width / 2 + 61, y + 85, x + width - 8, y + 85);
    }

    private static void DrawCenteredPair(
        XGraphics graphics,
        string label,
        string value,
        double x,
        double y,
        double width,
        double height,
        XFont labelFont,
        XFont valueFont)
    {
        graphics.DrawString(
            label,
            labelFont,
            new XSolidBrush(Muted),
            new XRect(x, y + 2, width, 13),
            XStringFormats.Center);
        graphics.DrawString(
            string.IsNullOrWhiteSpace(value) ? "-" : value,
            valueFont,
            new XSolidBrush(Ink),
            new XRect(x + 3, y + 14, width - 6, 13),
            XStringFormats.Center);
    }

    private static void DrawFooter(XGraphics graphics)
    {
        graphics.DrawLine(new XPen(Line, 0.45), Left, 568, Left + ContentWidth, 568);
        graphics.DrawString(
            "第 1 页 / 共 1 页",
            CreateFont(7, XFontStyleEx.Regular),
            new XSolidBrush(Muted),
            new XRect(Left, 574, ContentWidth, 12),
            XStringFormats.Center);
    }

    private static void DrawSectionHeader(
        XGraphics graphics,
        string title,
        double y,
        double x,
        double width)
    {
        graphics.DrawRectangle(new XSolidBrush(Pale), x, y, width, 20);
        graphics.DrawRectangle(new XPen(Line, 0.6), x, y, width, 20);
        graphics.DrawString(
            title,
            CreateFont(9.5, XFontStyleEx.Bold),
            new XSolidBrush(Ink),
            new XRect(x, y, width, 20),
            XStringFormats.Center);
    }

    private static void DrawInfo(
        XGraphics graphics,
        string label,
        string value,
        double x,
        double y,
        double labelWidth,
        double valueWidth,
        XFont labelFont,
        XFont valueFont)
    {
        graphics.DrawString(label, labelFont, new XSolidBrush(Muted), x, y);
        DrawWrappedText(
            graphics,
            string.IsNullOrWhiteSpace(value) ? "-" : value,
            valueFont,
            new XRect(x + labelWidth, y - 1, valueWidth, 13),
            1,
            XStringFormats.TopLeft);
    }

    private static void DrawCellText(
        XGraphics graphics,
        string text,
        XFont font,
        double x,
        double y,
        double width,
        double height)
    {
        DrawWrappedText(
            graphics,
            string.IsNullOrWhiteSpace(text) ? "-" : text,
            font,
            new XRect(x + 3, y + 1, Math.Max(2, width - 6), Math.Max(4, height - 2)),
            2,
            XStringFormats.Center);
    }

    private static void DrawCellsCentered(
        XGraphics graphics,
        double x,
        double y,
        IReadOnlyList<double> widths,
        double height,
        IReadOnlyList<string> values,
        XFont font,
        XPen pen)
    {
        var offset = x;
        for (var index = 0; index < widths.Count; index++)
        {
            if (index > 0)
            {
                graphics.DrawLine(pen, offset, y, offset, y + height);
            }
            DrawWrappedText(
                graphics,
                values.ElementAtOrDefault(index) ?? string.Empty,
                font,
                new XRect(offset + 2, y + 2, Math.Max(2, widths[index] - 4), Math.Max(4, height - 4)),
                2,
                XStringFormats.Center);
            offset += widths[index];
        }
    }

    private static void DrawWrappedText(
        XGraphics graphics,
        string text,
        XFont font,
        XRect rect,
        int maxLines,
        XStringFormat format)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var character in text.Replace("\r", string.Empty).Replace("\n", " "))
        {
            var candidate = current + character;
            if (current.Length > 0 && graphics.MeasureString(candidate, font).Width > rect.Width)
            {
                lines.Add(current);
                current = character.ToString();
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0)
        {
            lines.Add(current);
        }
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        var visibleLines = lines.Take(maxLines).ToList();
        var lineHeight = font.Size * 1.32;
        var totalHeight = visibleLines.Count * lineHeight;
        var lineTop = rect.Top + Math.Max(0, (rect.Height - totalHeight) / 2);
        graphics.Save();
        graphics.IntersectClip(rect);
        for (var index = 0; index < visibleLines.Count; index++)
        {
            graphics.DrawString(
                visibleLines[index],
                font,
                new XSolidBrush(Ink),
                new XRect(rect.Left, lineTop + index * lineHeight, rect.Width, lineHeight),
                format);
        }
        graphics.Restore();
    }

    private static void TryDrawImage(
        XGraphics graphics,
        string path,
        double x,
        double y,
        double width,
        double height)
    {
        try
        {
            using var image = XImage.FromFile(path);
            var rotation = GetExifRotation(path);
            var effectiveWidth = rotation % 180 == 0 ? image.PixelWidth : image.PixelHeight;
            var effectiveHeight = rotation % 180 == 0 ? image.PixelHeight : image.PixelWidth;

            if (effectiveHeight > effectiveWidth)
            {
                rotation = (rotation + 90) % 360;
                effectiveWidth = rotation % 180 == 0 ? image.PixelWidth : image.PixelHeight;
                effectiveHeight = rotation % 180 == 0 ? image.PixelHeight : image.PixelWidth;
            }

            var scale = Math.Min(width / effectiveWidth, height / effectiveHeight);
            var drawWidth = image.PixelWidth * scale;
            var drawHeight = image.PixelHeight * scale;
            graphics.Save();
            graphics.TranslateTransform(x + width / 2, y + height / 2);
            graphics.RotateTransform(rotation);
            graphics.DrawImage(image, -drawWidth / 2, -drawHeight / 2, drawWidth, drawHeight);
            graphics.Restore();
        }
        catch
        {
            graphics.DrawString(
                "图片无法读取",
                CreateFont(7, XFontStyleEx.Regular),
                new XSolidBrush(Muted),
                new XRect(x, y, width, height),
                XStringFormats.Center);
        }
    }

    private static int GetExifRotation(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.FirstOrDefault()?.Metadata is not BitmapMetadata metadata)
            {
                return 0;
            }
            var orientation = Convert.ToUInt16(
                metadata.GetQuery("/app1/ifd/{ushort=274}") ?? (ushort)1,
                CultureInfo.InvariantCulture);
            return orientation switch
            {
                3 or 4 => 180,
                5 or 6 => 90,
                7 or 8 => 270,
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }

    private static string Combine(params string[] values) =>
        string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string Money(decimal value, bool included) =>
        included ? $"¥{value:0.00}" : "";

    private static XFont CreateFont(double size, XFontStyleEx style) =>
        PdfFontSupport.CreateFont(size, style);
}
