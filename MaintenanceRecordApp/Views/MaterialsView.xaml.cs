using System.Windows;
using System.Windows.Controls;
using MaintenanceRecordApp.Models;
using MaintenanceRecordApp.ViewModels;
using Microsoft.Win32;

namespace MaintenanceRecordApp.Views;

public partial class MaterialsView : UserControl
{
    public MaterialsView()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void ImportMaterials_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入材料数据表",
            Filter = "Excel 工作簿 (*.xlsx;*.xlsm)|*.xlsx;*.xlsm",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        try
        {
            var result = ViewModel.ImportMaterials(dialog.FileName);
            MessageBox.Show(
                $"材料库导入完成，共读取 {result.Materials.Count} 条。",
                "材料导入",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "材料导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存材料模板",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = "材料库导入模板.xlsx",
            AddExtension = true,
            InitialDirectory = ViewModel.LibraryDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.CreateMaterialTemplate(dialog.FileName);
            MessageBox.Show("材料模板已生成。", "模板已保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportMaterials_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Materials.Count == 0)
        {
            MessageBox.Show("材料库为空。", "导出材料库", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "导出材料库",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"材料库_{DateTime.Now:yyyyMMdd}.xlsx",
            AddExtension = true,
            InitialDirectory = ViewModel.LibraryDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.ExportMaterials(dialog.FileName);
            MessageBox.Show("材料库已导出。", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AddManualMaterial_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.AddMaterialFromEditor();
            NewMaterialNameBox.Focus();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "新增材料", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddMaterial_Click(object sender, RoutedEventArgs e)
    {
        var material = ViewModel.AddBlankMaterial();
        MaterialsGrid.SelectedItem = material;
        MaterialsGrid.ScrollIntoView(material);
        MaterialsGrid.Focus();
    }

    private void SaveMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCatalogMaterial is not { } material)
        {
            MessageBox.Show("请先选择一条材料。", "保存材料", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(material.Name))
        {
            MessageBox.Show("材料名称不能为空。", "保存材料", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            ViewModel.SaveMaterial(material);
            MessageBox.Show("材料已保存并同步到材料库模板。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "材料保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCatalogMaterial is not { } material)
        {
            MessageBox.Show("请先选择一条材料。", "删除材料", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(
                $"确定删除材料“{material.Name}”？",
                "删除材料",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            ViewModel.DeleteMaterial(material);
        }
    }
}
