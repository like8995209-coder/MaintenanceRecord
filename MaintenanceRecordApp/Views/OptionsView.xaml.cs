using System.Windows;
using System.Windows.Controls;
using MaintenanceRecordApp.ViewModels;

namespace MaintenanceRecordApp.Views;

public partial class OptionsView : UserControl
{
    public OptionsView()
    {
        InitializeComponent();
        RepairMethodList.SelectionChanged += (_, _) =>
            RepairMethodInput.Text = RepairMethodList.SelectedItem?.ToString() ?? string.Empty;
        RepairPersonList.SelectionChanged += (_, _) =>
            RepairPersonInput.Text = RepairPersonList.SelectedItem?.ToString() ?? string.Empty;
        LaborCompanyList.SelectionChanged += (_, _) =>
            LaborCompanyInput.Text = LaborCompanyList.SelectedItem?.ToString() ?? string.Empty;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void AddOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category })
        {
            return;
        }
        var input = GetInput(category);
        var value = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show("请输入新选项。", "选项管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ViewModel.AddOption(category, value);
        input.Clear();
    }

    private void RenameOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category })
        {
            return;
        }
        var list = GetList(category);
        var input = GetInput(category);
        if (list.SelectedItem is not string oldValue || string.IsNullOrWhiteSpace(input.Text))
        {
            MessageBox.Show("请选择原选项并输入新名称。", "选项管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ViewModel.RenameOption(category, oldValue, input.Text.Trim());
        input.Clear();
    }

    private void DeleteOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category })
        {
            return;
        }
        var list = GetList(category);
        if (list.SelectedItem is not string value)
        {
            MessageBox.Show("请选择要删除的选项。", "选项管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(
                $"确定删除“{value}”？",
                "删除选项",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            ViewModel.DeleteOption(category, value);
        }
    }

    private TextBox GetInput(string category) =>
        category switch
        {
            "repair_method" => RepairMethodInput,
            "repair_person" => RepairPersonInput,
            "labor_company" => LaborCompanyInput,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private ListBox GetList(string category) =>
        category switch
        {
            "repair_method" => RepairMethodList,
            "repair_person" => RepairPersonList,
            "labor_company" => LaborCompanyList,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
}
