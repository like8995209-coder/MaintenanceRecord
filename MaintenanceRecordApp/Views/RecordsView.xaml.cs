using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaintenanceRecordApp.ViewModels;
using Microsoft.Win32;

namespace MaintenanceRecordApp.Views;

public partial class RecordsView : UserControl
{
    public RecordsView()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void NewRecord_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NewRecord();
        ViewModel.SelectedPageIndex = 0;
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ClearRecordFilters();

    private void ExportLedger_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.MatchedRecordCount == 0)
        {
            MessageBox.Show(
                "当前筛选条件下没有可导出的台账记录。",
                "导出台账",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出筛选台账",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"管网维修台账_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            InitialDirectory = ViewModel.LibraryDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.ExportLedger(dialog.FileName);
            ShowExported(dialog.FileName);
        }
    }

    private void ExportTodayPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出今日情况",
            Filter = "PDF 文件 (*.pdf)|*.pdf",
            FileName = $"今日情况_{DateTime.Now:yyyyMMdd}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            InitialDirectory = ViewModel.TodayExportDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.ExportTodayReport(dialog.FileName);
            ShowExported(dialog.FileName);
        }
    }

    private void EditRecord_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void RecordsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void EditSelected()
    {
        if (ViewModel.SelectedRecord is null)
        {
            MessageBox.Show("请先选择一条记录。", "编辑记录", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ViewModel.LoadRecord(ViewModel.SelectedRecord);
    }

    private void GenerateSelectedPdf_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRecord is not { } record)
        {
            MessageBox.Show("请先选择一条记录。", "导出 PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!record.CanGenerate)
        {
            MessageBox.Show(
                record.CompletionSummary,
                "记录尚未完成",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var safeNumber = record.OrderNumber
            .Replace('/', '-')
            .Replace('\\', '-')
            .Replace(':', '-');
        var dialog = new SaveFileDialog
        {
            Title = "保存维修记录 PDF",
            Filter = "PDF 文件 (*.pdf)|*.pdf",
            FileName = $"{safeNumber}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            InitialDirectory = ViewModel.RecordExportDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.GeneratePdf(record, dialog.FileName);
            ShowExported(dialog.FileName);
        }
    }

    private void DeleteRecord_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRecord is null)
        {
            MessageBox.Show("请先选择一条记录。", "删除记录", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(
                $"确定删除记录 {ViewModel.SelectedRecord.OrderNumber}？\n此操作无法撤销。",
                "删除记录",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            ViewModel.DeleteRecord(ViewModel.SelectedRecord);
        }
    }

    private static void ShowExported(string path)
    {
        var result = MessageBox.Show(
            $"文件已导出：\n{path}\n\n是否立即打开？",
            "导出成功",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }
}
