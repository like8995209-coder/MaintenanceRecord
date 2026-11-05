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
                "U 盘备份或导入正在进行，请等待文件复制和校验完成后再退出。",
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

    private async void ImportFromUsb_Click(object sender, RoutedEventArgs e)
    {
        var drive = SelectSingleUsbDrive("导入");
        if (drive is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"检测到 U 盘：{drive.DisplayName}\n\n" +
                "程序将查找 U 盘中的“管网维修数据”或名称相近的备份文件夹，" +
                "并把缺失或内容不同的文件同步到本机。\n\n" +
                "导入可能更新本机台账数据库。程序会先自动创建当前数据库安全副本，但仍然建议先完成一次备份。\n\n" +
                "是否开始导入？",
                "一键U盘导入",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        SetUsbButtonsEnabled(false);
        _backupInProgress = true;
        MainContent.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        string? safetyBackup = null;
        try
        {
            safetyBackup = ViewModel.PrepareForUsbImport();
            var dataDirectory = ViewModel.DataDirectory;
            ViewModel.StatusMessage = "正在从 U 盘增量导入并校验文件，请稍候...";
            var result = await Task.Run(
                () => UsbBackupService.Import(dataDirectory, drive));
            ViewModel.CompleteUsbImport();
            MessageBox.Show(
                $"U 盘数据导入完成，文件校验无误。\n\n" +
                $"导入位置：{result.DestinationDirectory}\n" +
                $"新增或更新：{result.CopiedFiles} 个文件\n" +
                $"已存在跳过：{result.SkippedFiles} 个文件\n" +
                $"完整校验：{result.VerifiedFiles} 个文件\n" +
                $"导入前数据库副本：{safetyBackup}\n\n" +
                "请通过 Windows 任务栏“安全删除硬件并弹出媒体”安全弹出 U 盘后再拔出。",
                "导入与校验完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            var recovered = false;
            if (safetyBackup is not null)
            {
                try
                {
                    ViewModel.RestoreAfterFailedUsbImport(safetyBackup);
                    recovered = true;
                }
                catch
                {
                    recovered = false;
                }
            }

            var logPath = WriteUsbErrorLog("U盘导入失败", exception);
            MessageBox.Show(
                $"U 盘导入失败：\n{exception.Message}\n\n" +
                (recovered
                    ? "已自动恢复导入前的数据库。"
                    : "未能自动恢复数据库，请保留现场并检查诊断日志。") +
                $"\n\n安全副本：{safetyBackup ?? "未创建"}\n诊断日志：{logPath}",
                "导入失败",
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
        ImportUsbButton.IsEnabled = enabled;
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
