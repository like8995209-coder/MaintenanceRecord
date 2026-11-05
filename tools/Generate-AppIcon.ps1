$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common
$drawingAssembly = [System.Drawing.Bitmap].Assembly.Location
$gdiAssembly = [System.Reflection.Assembly]::Load(
    'System.Private.Windows.GdiPlus').Location
$windowsCoreAssembly = [System.Reflection.Assembly]::Load(
    'System.Private.Windows.Core').Location
$drawingPrimitivesAssembly = [System.Reflection.Assembly]::Load(
    'System.Drawing.Primitives').Location

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MaintenanceIconGenerator
{
    private const int DesignSize = 512;
    private static readonly int[] IconSizes = { 16, 24, 32, 48, 64, 128, 256 };

    public static void Generate(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var full = Render(512);
        full.Save(Path.Combine(outputDirectory, "app-icon-preview.png"), ImageFormat.Png);
        full.Save(Path.Combine(outputDirectory, "app.png"), ImageFormat.Png);

        var frames = new byte[IconSizes.Length][];
        for (var i = 0; i < IconSizes.Length; i++)
        {
            using var bitmap = Render(IconSizes[i]);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            frames[i] = stream.ToArray();
        }
        WriteIco(Path.Combine(outputDirectory, "app.ico"), frames);

        using var preview = new Bitmap(640, 360);
        using (var graphics = Graphics.FromImage(preview))
        {
            graphics.Clear(Color.FromArgb(243, 246, 244));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            DrawPreview(graphics, full, 38, 34, 256);
            DrawPreview(graphics, Render(64), 342, 48, 64);
            DrawPreview(graphics, Render(48), 432, 56, 48);
            DrawPreview(graphics, Render(32), 510, 64, 32);
            DrawPreview(graphics, Render(16), 574, 72, 16);
            using var font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
            using var muted = new SolidBrush(Color.FromArgb(90, 108, 101));
            graphics.DrawString("管网维修", font, muted, 38, 310);
            using var smallFont = new Font("Microsoft YaHei UI", 9, FontStyle.Regular);
            graphics.DrawString("16 / 32 / 48 / 64 / 256 px", smallFont, muted, 342, 152);
        }
        preview.Save(Path.Combine(outputDirectory, "app-icon-preview-sizes.png"), ImageFormat.Png);
        full.Dispose();
    }

    private static Bitmap Render(int size)
    {
        var compact = size <= 32;
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.ScaleTransform(size / (float)DesignSize, size / (float)DesignSize);

        using var backgroundPath = RoundedRectangle(8, 8, 496, 496, 112);
        using var background = new LinearGradientBrush(
            new RectangleF(16, 14, 480, 484),
            Color.FromArgb(3, 20, 42),
            Color.FromArgb(48, 210, 220),
            90f);
        graphics.FillPath(background, backgroundPath);

        var clipState = graphics.Save();
        graphics.SetClip(backgroundPath);
        using var centerGlow = new PathGradientBrush(
            new[]
            {
                new PointF(96, 92),
                new PointF(416, 92),
                new PointF(416, 420),
                new PointF(96, 420),
            })
        {
            CenterColor = Color.FromArgb(78, 42, 194, 236),
            SurroundColors = new[]
            {
                Color.FromArgb(0, 42, 194, 236),
                Color.FromArgb(0, 42, 194, 236),
                Color.FromArgb(0, 42, 194, 236),
                Color.FromArgb(0, 42, 194, 236),
            },
        };
        graphics.FillRectangle(centerGlow, 70, 60, 372, 390);
        using var topSheen = new LinearGradientBrush(
            new RectangleF(34, 28, 444, 210),
            Color.FromArgb(compact ? 72 : 155, Color.White),
            Color.FromArgb(0, Color.White),
            90f);
        graphics.FillEllipse(topSheen, 42, 20, 428, 210);
        using var glassStripe = new LinearGradientBrush(
            new RectangleF(48, 42, 300, 330),
            Color.FromArgb(compact ? 34 : 76, Color.White),
            Color.FromArgb(0, Color.White),
            135f);
        graphics.FillPolygon(
            glassStripe,
            new[]
            {
                new PointF(82, 52),
                new PointF(250, 34),
                new PointF(112, 292),
                new PointF(54, 230),
            });
        graphics.Restore(clipState);

        using var outerRim = new Pen(Color.FromArgb(210, 8, 54, 119), 10);
        graphics.DrawPath(outerRim, backgroundPath);
        using var innerRim = RoundedRectangle(17, 17, 478, 478, 104);
        using var innerRimPen = new Pen(Color.FromArgb(150, 130, 220, 246), 4);
        graphics.DrawPath(innerRimPen, innerRim);
        using var topHighlight = new Pen(Color.FromArgb(compact ? 150 : 215, Color.White), 5)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        graphics.DrawArc(topHighlight, 24, 20, 464, 464, 188, 92);
        using var bottomHighlight = new Pen(Color.FromArgb(135, 173, 246, 250), 4);
        graphics.DrawArc(bottomHighlight, 24, 20, 464, 464, 20, 92);

        using var characterPath = CreateCharacterPath(256, 278, compact);
        using var shadowPath = (GraphicsPath)characterPath.Clone();
        using (var shadowTransform = new Matrix())
        {
            shadowTransform.Translate(10, 11);
            shadowPath.Transform(shadowTransform);
        }
        using var shadowBrush = new SolidBrush(Color.FromArgb(155, 0, 12, 26));
        graphics.FillPath(shadowBrush, shadowPath);

        using var textGradient = new LinearGradientBrush(
            new RectangleF(86, 84, 345, 365),
            compact ? Color.FromArgb(246, 252, 253) : Color.FromArgb(164, 179, 190),
            compact ? Color.FromArgb(166, 230, 238) : Color.FromArgb(5, 12, 20),
            90f);
        if (!compact)
        {
            textGradient.InterpolationColors = new ColorBlend
            {
                Positions = new[] { 0f, 0.18f, 0.43f, 0.72f, 1f },
                Colors = new[]
                {
                    Color.FromArgb(164, 179, 190),
                    Color.FromArgb(55, 72, 84),
                    Color.FromArgb(9, 18, 28),
                    Color.FromArgb(22, 44, 58),
                    Color.FromArgb(5, 12, 20),
                },
            };
        }
        graphics.FillPath(textGradient, characterPath);
        using var edgeGlow = (GraphicsPath)characterPath.Clone();
        using (var glowTransform = new Matrix())
        {
            glowTransform.Translate(0, 2);
            edgeGlow.Transform(glowTransform);
        }
        using var edgeGlowPen = new Pen(Color.FromArgb(150, 4, 190, 222), 9)
        {
            LineJoin = LineJoin.Round,
        };
        graphics.DrawPath(edgeGlowPen, edgeGlow);
        graphics.FillPath(textGradient, characterPath);

        using var textRim = new Pen(
            compact
                ? Color.FromArgb(245, 10, 195, 222)
                : Color.FromArgb(218, 17, 205, 231),
            compact ? 7 : 6)
        {
            LineJoin = LineJoin.Round,
        };
        graphics.DrawPath(textRim, characterPath);

        using var innerHighlightPath = (GraphicsPath)characterPath.Clone();
        using (var highlightTransform = new Matrix())
        {
            highlightTransform.Translate(-3, -4);
            innerHighlightPath.Transform(highlightTransform);
        }
        using var innerHighlightPen = new Pen(
            compact ? Color.FromArgb(130, Color.White) : Color.FromArgb(155, Color.White),
            compact ? 2 : 1.8f);
        graphics.DrawPath(innerHighlightPen, innerHighlightPath);
        return bitmap;
    }

    private static GraphicsPath CreateCharacterPath(float centerX, float centerY, bool compact)
    {
        using var family = new FontFamily(compact ? "Microsoft YaHei" : "STXingkai");
        var path = new GraphicsPath();
        path.AddString(
            "修",
            family,
            (int)(compact ? FontStyle.Bold : FontStyle.Regular),
            compact ? 268 : 342,
            new PointF(0, 0),
            StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        using var transform = new Matrix();
        transform.Translate(-bounds.Left - bounds.Width / 2, -bounds.Top - bounds.Height / 2);
        transform.Scale(compact ? 1.02f : 0.96f, compact ? 1.02f : 1.03f);
        transform.Rotate(compact ? 0f : -5f);
        transform.Translate(centerX, centerY);
        path.Transform(transform);
        return path;
    }

    private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawPreview(Graphics graphics, Bitmap bitmap, int x, int y, int size)
    {
        graphics.DrawImage(bitmap, new Rectangle(x, y, size, size));
        if (!ReferenceEquals(bitmap, null))
        {
            bitmap.Dispose();
        }
    }

    private static void WriteIco(string path, byte[][] frames)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Length);

        var offset = 6 + frames.Length * 16;
        for (var i = 0; i < frames.Length; i++)
        {
            var size = IconSizes[i];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frames[i].Length);
            writer.Write((uint)offset);
            offset += frames[i].Length;
        }

        foreach (var frame in frames)
        {
            writer.Write(frame);
        }
    }
}
'@ -ReferencedAssemblies @(
    $drawingAssembly,
    $gdiAssembly,
    $windowsCoreAssembly,
    $drawingPrimitivesAssembly
)

$assetDirectory = Join-Path $PSScriptRoot '..\MaintenanceRecordApp\Assets'
[MaintenanceIconGenerator]::Generate(
    [System.IO.Path]::GetFullPath($assetDirectory))
