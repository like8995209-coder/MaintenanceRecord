using System.Windows;
using System.Threading;

namespace MaintenanceRecordApp;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\MaintenanceRecord.GuanWangWeiXiu";
    private Mutex? _instanceMutex;

    public App()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            MessageBox.Show(
                eventArgs.Exception.Message,
                "程序发生错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            eventArgs.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (HasAnotherInstance())
        {
            var decision = MessageBox.Show(
                "管网维修程序已经在运行。\n\n是否确定再次新开一个窗口？",
                "管网维修",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (decision != MessageBoxResult.Yes)
            {
                Shutdown();
                return;
            }
        }
        else
        {
            _instanceMutex = new Mutex(true, InstanceMutexName, out _);
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex can already be released during abnormal shutdown.
            }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }

    private static bool HasAnotherInstance()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(InstanceMutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }
}
