using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace MaintenanceRecordApp.Services;

internal static class PdfFontSupport
{
    private static readonly object SyncRoot = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }
            GlobalFontSettings.FontResolver = new WindowsChineseFontResolver();
            _initialized = true;
        }
    }

    public static XFont CreateFont(double size, XFontStyleEx style)
    {
        EnsureInitialized();
        return new XFont("Microsoft YaHei", size, style);
    }

    private sealed class WindowsChineseFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(bold ? "DengBold" : "DengRegular");

        public byte[] GetFont(string faceName) =>
            File.ReadAllBytes(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                    faceName == "DengBold" ? "Dengb.ttf" : "Deng.ttf"));
    }
}
