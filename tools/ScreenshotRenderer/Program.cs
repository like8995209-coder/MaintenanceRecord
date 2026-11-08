using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaintenanceRecordApp;
using MaintenanceRecordApp.Models;
using MaintenanceRecordApp.ViewModels;

namespace ScreenshotRenderer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var outputDirectory = args.FirstOrDefault() ??
            throw new ArgumentException("请提供截图输出目录。");
        Directory.CreateDirectory(outputDirectory);

        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow
        {
            Width = 1180,
            Height = 760,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();
        window.Width = 1180;
        window.Height = 760;

        var viewModel = (MainViewModel)window.DataContext;
        var sampleRecord = viewModel.Records.FirstOrDefault(record =>
            record.Status == MaintenanceRecordStatus.Completed);
        if (sampleRecord is not null)
        {
            viewModel.LoadRecord(sampleRecord);
        }
        viewModel.StatusMessage = "本地数据安全存储，一键 U 盘备份随时可用。";

        var pages = new[]
        {
            (Index: 0, FileName: "new-record.png"),
            (Index: 1, FileName: "records.png"),
            (Index: 2, FileName: "materials.png"),
            (Index: 3, FileName: "options.png"),
        };
        foreach (var page in pages)
        {
            viewModel.SelectedPageIndex = page.Index;
            window.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            Capture(
                content,
                Math.Max(1180, (int)Math.Ceiling(content.ActualWidth)),
                Math.Max(680, (int)Math.Ceiling(content.ActualHeight)),
                Path.Combine(outputDirectory, page.FileName));
        }

        Environment.Exit(0);
    }

    private static void Capture(
        FrameworkElement content,
        int width,
        int height,
        string path)
    {
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
        {
            drawing.DrawRectangle(
                Brushes.White,
                null,
                new Rect(0, 0, width, height));
        }
        bitmap.Render(background);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
