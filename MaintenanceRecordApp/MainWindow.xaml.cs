using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MaintenanceRecordApp.Services;
using MaintenanceRecordApp.ViewModels;

namespace MaintenanceRecordApp;

public partial class MainWindow : Window
{
    private bool _exitConfirmed;
    private bool _backupInProgress;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
        var viewModel = new MainViewModel();
        var pageArgument = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        if (pageArgument is not null &&
            int.TryParse(pageArgument["--page=".Length..], out var pageIndex))
        {
            viewModel.SelectedPageIndex = Math.Clamp(pageIndex, 0, 3);
        }
        DataContext = viewModel;
        SourceInitialized += (_, _) => FitToWorkArea();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_backupInProgress)
        {
            MessageBox.Show(
                "U 盘备份正在进行，请等待文件复制和校验完成后再退出。",
                "U盘操作进行中",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            e.Cancel = true;
            return;
        }
        if (_exitConfirmed)
        {
            return;
        }

        var result = MessageBox.Show(
            "退出程序前，请确认已完成数据备份。\n\n" +
            "可在左侧主菜单“选项管理”下方使用“一键U盘备份”。\n\n" +
            "已完成备份并退出程序吗？\n" +
            "选择“否”将返回程序。",
            "退出前备份提醒",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _exitConfirmed = true;
            return;
        }
        e.Cancel = true;
    }

    private async void BackupToUsb_Click(object sender, RoutedEventArgs e)
    {
        var drive = SelectSingleUsbDrive("备份");
        if (drive is null)
        {
            return;
        }
        if (MessageBox.Show(
                $"检测到 U 盘：{drive.DisplayName}\n\n" +
                "程序将创建或复用 U 盘中的“管网维修数据”文件夹，增量复制缺少的文件，" +
                "并在完成后逐个校验文件完整性。\n\n是否开始备份？",
                "一键U盘备份",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes) != MessageBoxResult.Yes)
        {
            return;
        }

        SetUsbButtonsEnabled(false);
        _backupInProgress = true;
        MainContent.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            ViewModel.PrepareForUsbBackup();
            var dataDirectory = ViewModel.DataDirectory;
            ViewModel.StatusMessage = "正在增量复制并校验 U 盘数据，请稍候...";
            var result = await Task.Run(
                () => UsbBackupService.Backup(dataDirectory, drive));
            ViewModel.StatusMessage =
                $"U 盘备份完成并校验：{result.DestinationDirectory}";
            MessageBox.Show(
                $"U 盘备份完成，文件校验无误。\n\n" +
                $"备份位置：{result.DestinationDirectory}\n" +
                $"新增或更新：{result.CopiedFiles} 个文件\n" +
                $"已存在跳过：{result.SkippedFiles} 个文件\n" +
                $"完整校验：{result.VerifiedFiles} 个文件\n\n" +
                "请通过 Windows 任务栏“安全删除硬件并弹出媒体”安全弹出 U 盘后再拔出。",
                "备份与校验完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            var logPath = WriteUsbErrorLog("U盘备份失败", exception);
            MessageBox.Show(
                $"U 盘备份失败：\n{exception.Message}\n\n诊断日志：\n{logPath}",
                "备份失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            SetUsbButtonsEnabled(true);
            MainContent.IsEnabled = true;
            _backupInProgress = false;
        }
    }

    private UsbDriveInfo? SelectSingleUsbDrive(string operationName)
    {
        var drives = ViewModel.DetectUsbDrives();
        if (drives.Count == 0)
        {
            MessageBox.Show(
                $"未检测到 U 盘或可移动存储设备。\n\n请插入 U 盘后重新点击“一键U盘{operationName}”。",
                "未检测到U盘",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }
        if (drives.Count > 1)
        {
            MessageBox.Show(
                "检测到多个可移动存储设备。\n\n" +
                "为避免操作错误的设备，请只保留需要使用的 U 盘，然后重新操作。\n\n" +
                string.Join("\n", drives.Select(drive => drive.DisplayName)),
                "请选择一个U盘",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }
        return drives[0];
    }

    private void SetUsbButtonsEnabled(bool enabled)
    {
        BackupUsbButton.IsEnabled = enabled;
    }

    private string WriteUsbErrorLog(string prefix, Exception exception)
    {
        var logPath = Path.Combine(
            ViewModel.LibraryDirectory,
            $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        try
        {
            Directory.CreateDirectory(ViewModel.LibraryDirectory);
            File.WriteAllText(
                logPath,
                $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"数据目录：{ViewModel.DataDirectory}{Environment.NewLine}{Environment.NewLine}" +
                exception);
            return logPath;
        }
        catch
        {
            return "日志写入失败";
        }
    }

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        WindowState = WindowState.Normal;
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Width = Math.Min(1280, Math.Max(940, workArea.Width - 32));
        Height = Math.Min(820, Math.Max(620, workArea.Height - 32));
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }
}
