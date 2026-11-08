using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MaintenanceRecordApp;
using MaintenanceRecordApp.Models;
using MaintenanceRecordApp.Services;
using MaintenanceRecordApp.ViewModels;
using MaintenanceRecordApp.Views;

namespace MaintenanceRecordTests;

public sealed class NewRecordViewBindingTests
{
    [Fact]
    public void VisibleEditableComboTextIsCopiedIntoDraftBeforeGeneration()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var viewModel = new MainViewModel();
                var view = new NewRecordView { DataContext = viewModel };
                var repairPerson = Assert.IsType<ComboBox>(view.FindName("RepairPersonCombo"));
                var repairMethod = Assert.IsType<ComboBox>(view.FindName("RepairMethodCombo"));
                repairPerson.Text = "李华";
                repairMethod.Text = "更换阀门";

                typeof(NewRecordView)
                    .GetMethod("CommitEditorValues", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(view, null);

                Assert.Equal("李华", viewModel.Draft.RepairPerson);
                Assert.Equal("更换阀门", viewModel.Draft.RepairMethod);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void SelectingMaterialAddsItsSpecificationAndPriceToTheFee()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var dataDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "MaintenanceRecordTests",
                    Guid.NewGuid().ToString("N"));
                var database = new DatabaseService(dataDirectory);
                database.ImportMaterials(
                [
                    new MaterialCatalogItem
                    {
                        Name = "哈夫节",
                        Specification = "DN40",
                        Unit = "个",
                        Price = 100m,
                    },
                ]);

                var viewModel = new MainViewModel(database);
                var pick = Assert.Single(viewModel.MaterialPicks);
                viewModel.SetMaterialSelected(pick, true);

                var selected = Assert.Single(viewModel.Draft.Materials);
                Assert.Equal("DN40", selected.Specification);
                Assert.Equal(100m, selected.UnitPrice);
                Assert.Equal(100m, viewModel.Draft.MaterialCost);
                Assert.Equal(100m, viewModel.TotalCost);

                viewModel.SetMaterialSelected(pick, false);
                Assert.Empty(viewModel.Draft.Materials);
                Assert.Equal(0m, viewModel.Draft.MaterialCost);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void SavingNewDropdownValuesDoesNotClearRequiredFields()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var dataDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "MaintenanceRecordTests",
                    Guid.NewGuid().ToString("N"));
                var database = new DatabaseService(dataDirectory);
                database.ImportMaterials(
                [
                    new MaterialCatalogItem
                    {
                        Name = "哈夫节",
                        Specification = "DN40",
                        Unit = "个",
                        Price = 100m,
                    },
                ]);

                var viewModel = new MainViewModel(database);
                var draft = viewModel.Draft;
                draft.RepairPerson = "新维修员";
                draft.RepairMethod = "新维修方式";
                draft.Location = "测试地点";
                draft.RepairReason = "测试原因";
                draft.WorkDate = DateTime.Today;
                draft.WorkClock = "09:00";
                draft.CompletionDate = DateTime.Today;
                draft.CompletionClock = "10:00";
                draft.Accepted = true;
                draft.PreparedBy = "测试制单人";
                viewModel.SetMaterialSelected(Assert.Single(viewModel.MaterialPicks), true);

                Assert.True(draft.CanGenerate);
                Assert.True(viewModel.SaveCurrentRecord());
                Assert.Equal("新维修员", draft.RepairPerson);
                Assert.Equal("新维修方式", draft.RepairMethod);

                var saved = Assert.Single(database.LoadRecords());
                Assert.Equal("新维修员", saved.RepairPerson);
                Assert.Equal("新维修方式", saved.RepairMethod);
                Assert.True(saved.CanGenerate);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void ParkedIncompleteAndCompletedStatesArePersisted()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var dataDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "MaintenanceRecordTests",
                    Guid.NewGuid().ToString("N"));
                var database = new DatabaseService(dataDirectory);
                database.ImportMaterials(
                [
                    new MaterialCatalogItem
                    {
                        Name = "哈夫节",
                        Specification = "DN40",
                        Unit = "个",
                        Price = 100m,
                    },
                ]);

                var viewModel = new MainViewModel(database);
                Assert.True(viewModel.ParkCurrentRecord());
                Assert.Equal(MaintenanceRecordStatus.Parked, database.LoadRecords().Single().Status);

                viewModel.NewRecord();
                var draft = viewModel.Draft;
                draft.Source = "热线";
                draft.RepairPerson = "李华";
                draft.Location = "测试地点";
                draft.RepairReason = "测试原因";
                draft.RepairMethod = "更换管件";
                draft.WorkDate = DateTime.Today;
                draft.WorkClock = "09:00";
                draft.CompletionDate = DateTime.Today;
                viewModel.SetMaterialSelected(Assert.Single(viewModel.MaterialPicks), true);
                Assert.True(viewModel.SaveCurrentRecord());
                Assert.Equal(
                    MaintenanceRecordStatus.Incomplete,
                    database.LoadRecords().Single(item => item.Id == draft.Id).Status);

                draft.CompletionClock = "10:00";
                draft.Accepted = true;
                draft.PreparedBy = "测试制单人";
                Assert.True(viewModel.SaveCurrentRecord());
                Assert.Equal(
                    MaintenanceRecordStatus.Completed,
                    database.LoadRecords().Single(item => item.Id == draft.Id).Status);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void SimilarMaterialInputUpdatesExistingMaterialInsteadOfDuplicating()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var dataDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "MaintenanceRecordTests",
                    Guid.NewGuid().ToString("N"));
                var database = new DatabaseService(dataDirectory);
                database.ImportMaterials(
                [
                    new MaterialCatalogItem
                    {
                        Name = "铜加密阀",
                        Specification = "DN20",
                        Unit = "个",
                        Price = 100m,
                    },
                    new MaterialCatalogItem
                    {
                        Name = "铜阀",
                        Specification = "DN25",
                        Unit = "个",
                        Price = 80m,
                    },
                ]);

                var viewModel = new MainViewModel(database);
                viewModel.NewMaterialName = "铜加密阀";
                Assert.Contains(
                    viewModel.SimilarCatalogMaterials,
                    material => material.Name == "铜加密阀");
                viewModel.NewMaterialSpecification = "DN20";
                viewModel.NewMaterialUnit = "个";
                viewModel.NewMaterialPriceText = "120";
                viewModel.AddMaterialFromEditor();

                var materials = database.LoadMaterials();
                Assert.Equal(2, materials.Count);
                var material = Assert.Single(materials, item => item.Name == "铜加密阀");
                Assert.Equal("铜加密阀", material.Name);
                Assert.Equal(120m, material.Price);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void LedgerFiltersLimitByDatePersonMethodAndStatus()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var dataDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "MaintenanceRecordTests",
                    Guid.NewGuid().ToString("N"));
                var database = new DatabaseService(dataDirectory);
                database.SaveRecord(new MaintenanceRecord
                {
                    OrderNumber = "JLYL-2026-GWB-1105-01",
                    WorkDate = new DateTime(2026, 11, 5),
                    RepairPerson = "李华",
                    RepairMethod = "抢修",
                    Status = MaintenanceRecordStatus.Completed,
                    StatusInitialized = true,
                });
                database.SaveRecord(new MaintenanceRecord
                {
                    OrderNumber = "JLYL-2026-GWB-1105-02",
                    WorkDate = new DateTime(2026, 11, 5),
                    RepairPerson = "王工",
                    RepairMethod = "更换阀门",
                    Status = MaintenanceRecordStatus.Parked,
                    StatusInitialized = true,
                });
                database.SaveRecord(new MaintenanceRecord
                {
                    OrderNumber = "JLYL-2026-GWB-1104-01",
                    WorkDate = new DateTime(2026, 11, 4),
                    RepairPerson = "李华",
                    RepairMethod = "抢修",
                    Status = MaintenanceRecordStatus.Incomplete,
                    StatusInitialized = true,
                });

                var viewModel = new MainViewModel(database);
                viewModel.FilterStartDate = new DateTime(2026, 11, 5);
                viewModel.FilterEndDate = new DateTime(2026, 11, 5);
                Assert.Equal(2, viewModel.MatchedRecordCount);

                viewModel.SelectedStatusFilter = "挂单";
                Assert.Equal(1, viewModel.MatchedRecordCount);
                Assert.Equal("王工", Assert.Single(viewModel.FilteredRecords.Cast<MaintenanceRecord>()).RepairPerson);

                viewModel.SelectedStatusFilter = "全部状态";
                viewModel.SelectedRepairMethodFilter = "抢修";
                Assert.Equal(1, viewModel.MatchedRecordCount);
                Assert.Equal("李华", Assert.Single(viewModel.FilteredRecords.Cast<MaintenanceRecord>()).RepairPerson);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void ReferenceTemplatesPopulateRatesAndCosts()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var dataDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "MaintenanceRecordTests",
                    Guid.NewGuid().ToString("N"));
                var viewModel = new MainViewModel(new DatabaseService(dataDirectory));
                var draft = viewModel.Draft;

                draft.HasMachinery = true;
                draft.MachineryType = "60型小挖";
                draft.MachineryTime = "2";
                Assert.Equal("145", draft.MachineryRateText);
                Assert.Equal(290m, draft.MachineryCost);

                draft.HasOddJob = true;
                draft.OddJobType = "瓦工";
                draft.OddJobPeople = 1;
                draft.OddJobTime = "8";
                Assert.Equal("25", draft.OddJobRateText);
                Assert.Equal(200m, draft.OddJobCost);
                draft.OddJobTime = "4";
                Assert.Equal(100m, draft.OddJobCost);

                draft.Restored = true;
                draft.RestorationType = "水泥混凝土路面还原";
                draft.RestorationLengthText = "2";
                draft.RestorationWidthText = "1";
                draft.RestorationHeightText = "1";
                Assert.Equal("328", draft.RestorationUnitPriceText);
                Assert.Equal(2m, draft.RestorationVolume);
                Assert.Equal(656m, draft.RestorationCost);
                draft.RestorationHeightText = "0.5";
                Assert.Equal(1m, draft.RestorationVolume);
                Assert.Equal(328m, draft.RestorationCost);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void UsbBackupCopiesDataWithoutCrossThreadAccessErrors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var testRoot = Path.Combine(
                    Path.GetTempPath(),
                    "MaintenanceRecordTests",
                    Guid.NewGuid().ToString("N"));
                var dataDirectory = Path.Combine(testRoot, "data");
                var usbRoot = Path.Combine(testRoot, "usb");
                Directory.CreateDirectory(usbRoot);
                var viewModel = new MainViewModel(new DatabaseService(dataDirectory));
                viewModel.Draft.Location = "跨线程备份测试地点";
                viewModel.Draft.RepairReason = "检查后台备份是否访问了界面线程对象";
                viewModel.PrepareForUsbBackup();

#pragma warning disable xUnit1031
                var result = Task.Run(
                        () => viewModel.BackupToUsb(
                            new UsbDriveInfo(usbRoot, "测试U盘", long.MaxValue)))
                    .GetAwaiter()
                    .GetResult();
#pragma warning restore xUnit1031

                Assert.True(result.VerifiedFiles > 0);
                Assert.True(File.Exists(Path.Combine(
                    result.DestinationDirectory,
                    "maintenance.db")));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
