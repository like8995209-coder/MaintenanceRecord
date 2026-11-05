using System.Globalization;
using System.IO;
using MaintenanceRecordApp.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MaintenanceRecordApp.Services;

public static class DailyReportPdfService
{
    private const double PageWidthMm = 210;
    private const double PageHeightMm = 297;
    private const double Left = 24;
    private const double ContentWidth = 547;
    private const double ContentBottom = 778;

    private static readonly XColor Ink = XColor.FromArgb(27, 37, 34);
    private static readonly XColor Muted = XColor.FromArgb(92, 107, 101);
    private static readonly XColor Accent = XColor.FromArgb(0, 102, 87);
    private static readonly XColor Pale = XColor.FromArgb(240, 247, 244);
    private static readonly XColor Line = XColor.FromArgb(166, 181, 174);
    private static readonly XColor White = XColor.FromArgb(255, 255, 255);

    public static void Generate(
        string outputPath,
        DateTime date,
        IReadOnlyList<MaintenanceRecord> todayRecords,
        IReadOnlyList<MaintenanceRecord> unfinishedRecords)
    {
        PdfFontSupport.EnsureInitialized();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);

        using var document = new PdfDocument();
        document.Info.Title = $"今日管网维修情况 {date:yyyy.MM.dd}";
        document.Info.Author = "且萌且机制";
        document.Info.Subject = "今日维修情况";

        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(PageWidthMm);
        page.Height = XUnit.FromMillimeter(PageHeightMm);
        using var graphics = XGraphics.FromPdfPage(page);

        DrawTitle(graphics, date);
        DrawSummary(graphics, todayRecords, unfinishedRecords, 96);

        const double rowsTop = 209;
        const double sectionTitleHeight = 20;
        const double tableHeaderHeight = 25;
        const double sectionGap = 14;
        var rowCount = Math.Max(1, todayRecords.Count) + Math.Max(1, unfinishedRecords.Count);
        var availableRowsHeight = ContentBottom -
                                  (rowsTop + tableHeaderHeight + sectionTitleHeight + sectionGap +
                                   tableHeaderHeight + sectionTitleHeight);
        var rowHeight = Math.Clamp(availableRowsHeight / Math.Max(1, rowCount), 8.5, 42);
        var fontSize = Math.Clamp(rowHeight * 0.34, 5.2, 10.8);

        var y = rowsTop;
        DrawSectionTitle(graphics, "今日维修记录", todayRecords.Count, y);
        y += sectionTitleHeight;
        y = DrawRecordTable(graphics, todayRecords, y, rowHeight, fontSize, "今日暂无维修记录");

        y += sectionGap;
        DrawSectionTitle(graphics, "未完毕清单（全部）", unfinishedRecords.Count, y);
        y += sectionTitleHeight;
        DrawRecordTable(graphics, unfinishedRecords, y, rowHeight, fontSize, "暂无未完毕记录");

        DrawFooter(graphics);
        document.Save(outputPath);
    }

    private static void DrawTitle(XGraphics graphics, DateTime date)
    {
        graphics.DrawString(
            "今日管网维修情况",
            CreateFont(23, XFontStyleEx.Bold),
            new XSolidBrush(Ink),
            new XRect(Left, 28, ContentWidth, 34),
            XStringFormats.Center);
        graphics.DrawString(
            date.ToString("yyyy年MM月dd日", CultureInfo.CurrentCulture),
            CreateFont(9.5, XFontStyleEx.Regular),
            new XSolidBrush(Muted),
            new XRect(Left, 62, ContentWidth, 18),
            XStringFormats.Center);
        graphics.DrawLine(new XPen(Line, 0.7), Left, 84, Left + ContentWidth, 84);
    }

    private static void DrawSummary(
        XGraphics graphics,
        IReadOnlyList<MaintenanceRecord> todayRecords,
        IReadOnlyList<MaintenanceRecord> unfinishedRecords,
        double y)
    {
        var cards = new[]
        {
            ("今日事件", todayRecords.Count.ToString(CultureInfo.InvariantCulture)),
            ("今日已完成", todayRecords.Count(item => item.Status == MaintenanceRecordStatus.Completed).ToString(CultureInfo.InvariantCulture)),
            ("未完毕总数", unfinishedRecords.Count.ToString(CultureInfo.InvariantCulture)),
            ("今日成本", $"¥{todayRecords.Sum(item => item.TotalCost):0.00}"),
        };
        const double gap = 7;
        var cardWidth = (ContentWidth - gap * 3) / 4;
        for (var index = 0; index < cards.Length; index++)
        {
            var x = Left + index * (cardWidth + gap);
            graphics.DrawRectangle(new XSolidBrush(Pale), x, y, cardWidth, 64);
            graphics.DrawRectangle(new XPen(Line, 0.5), x, y, cardWidth, 64);
            graphics.DrawString(
                cards[index].Item1,
                CreateFont(8.2, XFontStyleEx.Regular),
                new XSolidBrush(Muted),
                new XRect(x, y + 8, cardWidth, 16),
                XStringFormats.Center);
            graphics.DrawString(
                cards[index].Item2,
                CreateFont(17, XFontStyleEx.Bold),
                new XSolidBrush(Accent),
                new XRect(x, y + 24, cardWidth, 30),
                XStringFormats.Center);
        }
    }

    private static void DrawSectionTitle(
        XGraphics graphics,
        string title,
        int count,
        double y)
    {
        graphics.DrawRectangle(new XSolidBrush(Pale), Left, y, ContentWidth, 20);
        graphics.DrawRectangle(new XPen(Line, 0.55), Left, y, ContentWidth, 20);
        graphics.DrawString(
            $"{title}（共 {count} 条）",
            CreateFont(10.2, XFontStyleEx.Bold),
            new XSolidBrush(Ink),
            new XRect(Left, y, ContentWidth, 20),
            XStringFormats.Center);
    }

    private static double DrawRecordTable(
        XGraphics graphics,
        IReadOnlyList<MaintenanceRecord> records,
        double y,
        double rowHeight,
        double fontSize,
        string emptyText)
    {
        const double headerHeight = 25;
        var columns = new[] { 0.234, 0.139, 0.17, 0.194, 0.091, 0.077, 0.095 };
        var widths = columns.Select(value => value * ContentWidth).ToArray();
        var headers = new[] { "维修编号", "接单时间", "维修位置", "事件与联系方式", "维修人", "状态", "费用" };
        var pen = new XPen(Line, 0.55);

        graphics.DrawRectangle(new XSolidBrush(Pale), Left, y, ContentWidth, headerHeight);
        graphics.DrawRectangle(pen, Left, y, ContentWidth, headerHeight);
        DrawCellsCentered(
            graphics,
            Left,
            y,
            widths,
            headerHeight,
            headers,
            Math.Max(7.1, Math.Min(8.7, fontSize)),
            XFontStyleEx.Bold,
            pen);
        y += headerHeight;

        if (records.Count == 0)
        {
            graphics.DrawRectangle(new XSolidBrush(White), Left, y, ContentWidth, rowHeight);
            graphics.DrawRectangle(pen, Left, y, ContentWidth, rowHeight);
            graphics.DrawString(
                emptyText,
                CreateFont(Math.Max(6.5, Math.Min(9, fontSize)), XFontStyleEx.Regular),
                new XSolidBrush(Muted),
                new XRect(Left, y, ContentWidth, rowHeight),
                XStringFormats.Center);
            return y + rowHeight;
        }

        foreach (var record in records)
        {
            var values = new[]
            {
                record.OrderNumber,
                record.WorkDateTimeText,
                record.Location,
                string.Join(" · ", new[] { record.RepairReason, record.Contact }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
                record.RepairPerson,
                record.StatusText,
                $"¥{record.TotalCost:0.00}",
            };
            graphics.DrawRectangle(new XSolidBrush(White), Left, y, ContentWidth, rowHeight);
            graphics.DrawRectangle(pen, Left, y, ContentWidth, rowHeight);
            DrawCellsCentered(
                graphics,
                Left,
                y,
                widths,
                rowHeight,
                values,
                fontSize,
                XFontStyleEx.Regular,
                pen);
            y += rowHeight;
        }
        return y;
    }

    private static void DrawCellsCentered(
        XGraphics graphics,
        double x,
        double y,
        IReadOnlyList<double> widths,
        double height,
        IReadOnlyList<string> values,
        double fontSize,
        XFontStyleEx style,
        XPen pen)
    {
        var offset = x;
        for (var index = 0; index < widths.Count; index++)
        {
            if (index > 0)
            {
                graphics.DrawLine(pen, offset, y, offset, y + height);
            }
            var text = values.ElementAtOrDefault(index) ?? string.Empty;
            var maxLines = height >= 34 ? 3 : height >= 16 ? 2 : 1;
            var cellWidth = Math.Max(2, widths[index] - 8);
            var offeredSize = GetCellFontSize(index, fontSize);
            var fittedSize = FitCellFontSize(
                graphics,
                text,
                offeredSize,
                style,
                cellWidth,
                height - 4,
                maxLines);
            DrawWrappedTextCentered(
                graphics,
                text,
                CreateFont(fittedSize, style),
                new XRect(offset + 2, y + 2, Math.Max(2, widths[index] - 4), Math.Max(4, height - 4)),
                maxLines);
            offset += widths[index];
        }
    }

    private static double GetCellFontSize(int index, double fontSize) =>
        index switch
        {
            0 => Math.Min(fontSize, 8.2),
            1 => Math.Min(fontSize, 8.2),
            5 => Math.Min(fontSize, 9.2),
            _ => fontSize,
        };

    private static double FitCellFontSize(
        XGraphics graphics,
        string text,
        double size,
        XFontStyleEx style,
        double width,
        double height,
        int maxLines)
    {
        for (var candidate = size; candidate >= 5.2; candidate -= 0.2)
        {
            var font = CreateFont(candidate, style);
            var lines = WrapLines(graphics, text, font, width);
            if (lines.Count <= maxLines && lines.Count * candidate * 1.28 <= height)
            {
                return candidate;
            }
        }
        return 5.2;
    }

    private static void DrawWrappedTextCentered(
        XGraphics graphics,
        string text,
        XFont font,
        XRect rect,
        int maxLines)
    {
        var lines = WrapLines(graphics, text, font, rect.Width);
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
                XStringFormats.Center);
        }
        graphics.Restore();
    }

    private static List<string> WrapLines(
        XGraphics graphics,
        string text,
        XFont font,
        double width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var character in text.Replace("\r", string.Empty).Replace("\n", " "))
        {
            var candidate = current + character;
            if (current.Length > 0 && graphics.MeasureString(candidate, font).Width > width)
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
        return lines;
    }

    private static void DrawFooter(XGraphics graphics)
    {
        graphics.DrawLine(new XPen(Line, 0.45), Left, 798, Left + ContentWidth, 798);
        graphics.DrawString(
            "第 1 页 / 共 1 页",
            CreateFont(7.8, XFontStyleEx.Regular),
            new XSolidBrush(Muted),
            new XRect(Left, 805, ContentWidth, 13),
            XStringFormats.Center);
    }

    private static XFont CreateFont(double size, XFontStyleEx style) =>
        PdfFontSupport.CreateFont(size, style);
}
