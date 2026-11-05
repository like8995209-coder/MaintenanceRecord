using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaintenanceRecordApp.Models;
using MaintenanceRecordApp.ViewModels;
using Microsoft.Win32;

namespace MaintenanceRecordApp.Views;

public partial class NewRecordView : UserControl
{
    private readonly DispatcherTimer _formSyncTimer;
    private bool _syncingForm;

    public NewRecordView()
    {
        InitializeComponent();
        RepairPersonCombo.AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(EditableCombo_TextChanged));
        RepairMethodCombo.AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(EditableCombo_TextChanged));
        LaborCompanyCombo.AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(EditableCombo_TextChanged));
        OddJobTypeCombo.AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(EditableCombo_TextChanged));
        MachineryTypeCombo.AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(EditableCombo_TextChanged));
        RestorationTypeCombo.AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(EditableCombo_TextChanged));
        _formSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _formSyncTimer.Tick += (_, _) => SynchronizeFormValues();
        Loaded += (_, _) => _formSyncTimer.Start();
        Unloaded += (_, _) => _formSyncTimer.Stop();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void NewRecord_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NewRecord();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => SynchronizeFormValues()));
    }

    private void ImportMaterials_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择材料数据表",
            Filter = "Excel 工作簿 (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var result = ViewModel.ImportMaterials(dialog.FileName);
            var warning = result.Warnings.Count == 0
                ? string.Empty
                : $"\n\n提示：\n{string.Join("\n", result.Warnings.Take(8))}";
            MessageBox.Show(
                $"成功读取并导入 {result.Materials.Count} 条材料。{warning}",
                "材料导入",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "材料导入失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AddPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category })
        {
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "选择施工照片",
            Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.AddPhotos(category, dialog.FileNames);
        }
    }

    private void ClearPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string category })
        {
            ViewModel.ClearPhotos(category);
        }
    }

    private void RemovePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string path } button)
        {
            return;
        }
        var itemsControl = FindAncestor<ItemsControl>(button);
        if (itemsControl?.Tag is string category)
        {
            ViewModel.RemovePhoto(category, path);
        }
    }

    private void RemoveMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RecordMaterial material })
        {
            ViewModel.DeleteSelectedMaterial(material);
        }
    }

    private void MaterialSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: MaterialPickItem material } checkBox)
        {
            ViewModel.SetMaterialSelected(material, checkBox.IsChecked == true);
        }
    }

    private void SaveRecord_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitEditorValues();
            if (ViewModel.SaveCurrentRecord())
            {
                MessageBox.Show(
                    ViewModel.Draft.StatusText == "已完成"
                        ? "台账记录已保存，资料完整，可一键导出。"
                        : "台账记录已保存。未完成内容会以浅红底显示在台账中。",
                    "保存成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "保存失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ParkRecord_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitEditorValues();
            if (ViewModel.ParkCurrentRecord())
            {
                MessageBox.Show(
                    "当前记录已挂单，并可在台账中继续编辑。",
                    "挂单成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "挂单失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void GeneratePdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitEditorValues();
            if (!ViewModel.Draft.CanGenerate)
            {
                MessageBox.Show(
                    ViewModel.Draft.CompletionSummary,
                    "资料尚未填写完整",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Directory.CreateDirectory(ViewModel.RecordExportDirectory);
            var safeNumber = ViewModel.Draft.OrderNumber
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
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var outputPath = ViewModel.GenerateCurrentPdf(dialog.FileName);
            if (outputPath is null)
            {
                MessageBox.Show(
                    ViewModel.Draft.CompletionSummary,
                    "资料尚未填写完整",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(
                $"维修记录已导出：\n{outputPath}",
                "导出成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo
            {
                FileName = outputPath,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "生成维修记录失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ExportToday_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitEditorValues();
            Directory.CreateDirectory(ViewModel.TodayExportDirectory);
            var dialog = new SaveFileDialog
            {
                Title = "保存今日情况 PDF",
                Filter = "PDF 文件 (*.pdf)|*.pdf",
                FileName = $"今日情况_{DateTime.Today:yyyyMMdd}.pdf",
                AddExtension = true,
                DefaultExt = ".pdf",
                InitialDirectory = ViewModel.TodayExportDirectory,
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var outputPath = ViewModel.ExportTodayReport(dialog.FileName);
            MessageBox.Show(
                $"今日情况已导出：\n{outputPath}",
                "导出成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo { FileName = outputPath, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "导出今日情况失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void EditableCombo_LostFocus(object sender, RoutedEventArgs e) =>
        ScheduleComboCommit(sender);

    private void EditableCombo_DropDownClosed(object? sender, EventArgs e) =>
        ScheduleComboCommit(sender);

    private void EditableCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ScheduleComboCommit(sender);

    private void EditableCombo_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            CommitComboValue(comboBox);
        }
    }

    private void EditableCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            CommitComboValue(comboBox);
        }
    }

    private void ScheduleComboCommit(object? sender)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (sender is ComboBox combo)
                {
                    BindingOperations.GetBindingExpression(combo, ComboBox.TextProperty)
                        ?.UpdateSource();
                    CommitComboValue(combo);
                }
            }));
    }

    private void EditableCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ComboBox combo)
        {
            return;
        }
        BindingOperations.GetBindingExpression(combo, ComboBox.TextProperty)?.UpdateSource();
        CommitComboValue(combo);
    }

    private void CommitEditorValues()
    {
        SelectedMaterialsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SelectedMaterialsGrid.CommitEdit(DataGridEditingUnit.Row, true);

        foreach (var textBox in FindVisualChildren<TextBox>(this))
        {
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();
        }
        foreach (var comboBox in FindVisualChildren<ComboBox>(this))
        {
            BindingOperations.GetBindingExpression(comboBox, ComboBox.TextProperty)?.UpdateSource();
        }
        foreach (var datePicker in FindVisualChildren<DatePicker>(this))
        {
            BindingOperations.GetBindingExpression(datePicker, DatePicker.SelectedDateProperty)
                ?.UpdateSource();
        }
        SynchronizeFormValues(force: true);
    }

    private void CommitComboValue(ComboBox comboBox)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (ReferenceEquals(comboBox, RepairPersonCombo))
        {
            viewModel.Draft.RepairPerson = comboBox.Text.Trim();
        }
        else if (ReferenceEquals(comboBox, RepairMethodCombo))
        {
            viewModel.Draft.RepairMethod = comboBox.Text.Trim();
        }
        else if (ReferenceEquals(comboBox, LaborCompanyCombo))
        {
            viewModel.Draft.LaborCompany = comboBox.Text.Trim();
        }
        else if (ReferenceEquals(comboBox, OddJobTypeCombo))
        {
            viewModel.Draft.OddJobType = comboBox.Text.Trim();
        }
        else if (ReferenceEquals(comboBox, MachineryTypeCombo))
        {
            viewModel.Draft.MachineryType = comboBox.Text.Trim();
        }
        else if (ReferenceEquals(comboBox, RestorationTypeCombo))
        {
            viewModel.Draft.RestorationType = comboBox.Text.Trim();
        }
        viewModel.Draft.NotifyAll();
    }

    private void SynchronizeFormValues(bool force = false)
    {
        if (_syncingForm ||
            (!force && !IsLoaded) ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _syncingForm = true;
        try
        {
            var draft = viewModel.Draft;
            draft.OrderNumber = (viewModel.Draft.OrderNumber).Trim();
            draft.ExternalOrderNumber = ExternalOrderNumberTextBox.Text.Trim();
            draft.Source = SourceCombo.SelectedItem?.ToString() ?? SourceCombo.Text.Trim();
            draft.RepairPerson = RepairPersonCombo.Text.Trim();
            draft.RepairMethod = RepairMethodCombo.Text.Trim();
            draft.Location = LocationTextBox.Text.Trim();
            draft.RepairReason = RepairReasonTextBox.Text.Trim();
            draft.Contact = ContactTextBox.Text.Trim();
            draft.WorkDate = WorkDatePicker.SelectedDate;
            draft.WorkClock = WorkTimeTextBox.Text.Trim();
            draft.CompletionDate = CompletionDatePicker.SelectedDate;
            draft.CompletionClock = CompletionTimeTextBox.Text.Trim();
            draft.Notes = NotesTextBox.Text.Trim();

            draft.HasOddJob = OddJobYesRadio.IsChecked == true;
            draft.OddJobTime = OddJobTimeTextBox.Text.Trim();
            draft.OddJobType = OddJobTypeCombo.Text.Trim();
            draft.OddJobPeople = int.TryParse(OddJobPeopleTextBox.Text.Trim(), out var oddJobPeople)
                ? Math.Max(0, oddJobPeople)
                : 1;
            if (!string.IsNullOrWhiteSpace(OddJobRateTextBox.Text))
            {
                draft.OddJobRateText = OddJobRateTextBox.Text.Trim();
            }
            draft.LaborCompany = LaborCompanyCombo.Text.Trim();
            if (!string.IsNullOrWhiteSpace(OddJobCostTextBox.Text))
            {
                draft.OddJobCostText = OddJobCostTextBox.Text.Trim();
            }
            draft.HasMachinery = MachineryYesRadio.IsChecked == true;
            draft.MachineryTime = MachineryTimeTextBox.Text.Trim();
            draft.MachineryType = MachineryTypeCombo.Text.Trim();
            if (!string.IsNullOrWhiteSpace(MachineryRateTextBox.Text))
            {
                draft.MachineryRateText = MachineryRateTextBox.Text.Trim();
            }
            if (!string.IsNullOrWhiteSpace(MachineryCostTextBox.Text))
            {
                draft.MachineryCostText = MachineryCostTextBox.Text.Trim();
            }
            draft.Restored = RestoredYesRadio.IsChecked == true;
            draft.RestorationType = RestorationTypeCombo.Text.Trim();
            if (!string.IsNullOrWhiteSpace(RestorationUnitPriceTextBox.Text))
            {
                draft.RestorationUnitPriceText = RestorationUnitPriceTextBox.Text.Trim();
            }
            draft.RestorationAreaText = RestorationAreaTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(RestorationCostTextBox.Text))
            {
                draft.RestorationCostText = RestorationCostTextBox.Text.Trim();
            }

            draft.Accepted = AcceptedYesRadio.IsChecked == true
                ? true
                : AcceptedNoRadio.IsChecked == true
                    ? false
                    : null;
            draft.PreparedBy = PreparedByTextBox.Text.Trim();
            draft.ReviewDepartment = ReviewDepartmentTextBox.Text.Trim();
            draft.ResponsiblePerson = ResponsiblePersonTextBox.Text.Trim();
            draft.NotifyAll();
        }
        finally
        {
            _syncingForm = false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                yield return typed;
            }
            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
