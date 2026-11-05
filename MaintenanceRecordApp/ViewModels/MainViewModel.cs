using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MaintenanceRecordApp.Models;
using MaintenanceRecordApp.Services;

namespace MaintenanceRecordApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _database;
    private readonly JsonSerializerOptions _cloneOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly DispatcherTimer _autoSaveTimer;
    private MaintenanceRecord _draft = new();
    private bool _autoSaveEnabled;
    private bool _suppressAutoSave;
    private bool _applyingReferenceValue;

    private static readonly IReadOnlyDictionary<string, decimal> OddJobReferenceRates =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["挖工"] = 15m,
            ["辅助管道工"] = 18.75m,
            ["管道工"] = 24.375m,
        };

    private static readonly IReadOnlyDictionary<string, decimal> MachineryReferenceRates =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["60型小挖"] = 145m,
            ["120/150型中挖"] = 210m,
            ["200型大挖"] = 270m,
        };

    private static readonly IReadOnlyDictionary<string, decimal> RestorationReferenceRates =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["素土（原土）回填还原"] = 37m,
            ["砂石（级配碎石）路面还原"] = 80m,
            ["水泥混凝土路面还原"] = 328m,
            ["沥青（黑化）路面还原"] = 300m,
            ["12cm改性沥青面层＋原水泥砼路面"] = 867m,
            ["水泥方砖人行道"] = 160m,
            ["彩色方砖人行道"] = 200m,
            ["现浇人行道"] = 160m,
            ["条（料）石路面"] = 300m,
            ["站石、卧石（路缘石）"] = 120m,
            ["外运多余土方（或运回填土）"] = 80m,
        };

    public MainViewModel(DatabaseService? database = null)
    {
        _database = database ?? new DatabaseService();
        Materials = [];
        MaterialPicks = [];
        Records = [];
        RepairMethods = [];
        RepairPeople = [];
        LaborCompanies = [];
        OddJobTypes = [];
        MachineryTypes = [];
        RestorationTypes = [];
        SimilarCatalogMaterials = [];
        RepairPersonFilters = ["全部人员"];
        RepairMethodFilters = ["全部方式"];

        FilteredMaterials = CollectionViewSource.GetDefaultView(MaterialPicks);
        FilteredMaterials.Filter = FilterMaterial;
        FilteredCatalog = CollectionViewSource.GetDefaultView(Materials);
        FilteredCatalog.Filter = FilterCatalogMaterial;
        FilteredRecords = CollectionViewSource.GetDefaultView(Records);
        FilteredRecords.Filter = FilterRecord;

        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(850),
        };
        _autoSaveTimer.Tick += AutoSaveTimerOnTick;

        AttachDraft(_draft);
        ReloadRecords();
        ReloadMaterials();
        ReloadOptions();
        NewRecord();
    }

    public ObservableCollection<MaterialCatalogItem> Materials { get; }

    public ObservableCollection<MaterialPickItem> MaterialPicks { get; }

    public ObservableCollection<MaintenanceRecord> Records { get; }

    public ObservableCollection<string> RepairMethods { get; }

    public ObservableCollection<string> RepairPeople { get; }

    public ObservableCollection<string> LaborCompanies { get; }

    public ObservableCollection<string> OddJobTypes { get; }

    public ObservableCollection<string> MachineryTypes { get; }

    public ObservableCollection<string> RestorationTypes { get; }

    public ObservableCollection<MaterialCatalogItem> SimilarCatalogMaterials { get; }

    public ObservableCollection<string> RepairPersonFilters { get; }

    public ObservableCollection<string> RepairMethodFilters { get; }

    public ICollectionView FilteredMaterials { get; }

    public ICollectionView FilteredCatalog { get; }

    public ICollectionView FilteredRecords { get; }

    public IReadOnlyList<string> StatusFilterOptions { get; } =
        ["全部状态", "已完成", "未完成", "挂单"];

    public IReadOnlyList<string> SourceOptions { get; } =
        ["客服转单", "热线", "巡检", "外业", "其他"];

    public IReadOnlyList<string> NavigationItems { get; } =
        ["新建记录", "台账记录", "材料库", "选项管理"];

    public string DataDirectory => _database.DataDirectory;

    public string TodayExportDirectory => _database.TodayReportsDirectory;

    public string RecordExportDirectory => _database.RecordReportsDirectory;

    public string LibraryDirectory => _database.LibraryDirectory;

    public string ImagesDirectory => _database.ImagesDirectory;

    public string MaterialTemplatePath => Path.Combine(LibraryDirectory, "材料库导入模板.xlsx");

    public MaintenanceRecord Draft
    {
        get => _draft;
        private set
        {
            if (SetProperty(ref _draft, value))
            {
                NotifyDraftCalculatedProperties();
            }
        }
    }

    public bool CanGenerate => Draft.CanGenerate;

    public decimal TotalCost => Draft.TotalCost;

    public decimal MaterialCost => Draft.MaterialCost;

    public int RecordCount => Records.Count;

    public int TodayCount => Records.Count(item => item.WorkDate?.Date == DateTime.Today);

    public int UnfinishedCount => Records.Count(item => item.Status != MaintenanceRecordStatus.Completed);

    public int CompletedCount => Records.Count(item => item.Status == MaintenanceRecordStatus.Completed);

    public int MatchedRecordCount => FilteredRecords.Cast<object>().Count();

    public int MaterialCount => Materials.Count;

    public bool HasSimilarCatalogMaterials => SimilarCatalogMaterials.Count > 0;

    [ObservableProperty]
    private int _selectedPageIndex;

    [ObservableProperty]
    private string _searchMaterialText = string.Empty;

    [ObservableProperty]
    private string _materialSuggestionText = string.Empty;

    [ObservableProperty]
    private string _searchRecordText = string.Empty;

    [ObservableProperty]
    private string _searchCatalogText = string.Empty;

    [ObservableProperty]
    private string _selectedStatusFilter = "全部状态";

    [ObservableProperty]
    private string _selectedRepairPersonFilter = "全部人员";

    [ObservableProperty]
    private string _selectedRepairMethodFilter = "全部方式";

    [ObservableProperty]
    private DateTime? _filterStartDate;

    [ObservableProperty]
    private DateTime? _filterEndDate;

    [ObservableProperty]
    private MaintenanceRecord? _selectedRecord;

    [ObservableProperty]
    private MaterialCatalogItem? _selectedCatalogMaterial;

    [ObservableProperty]
    private string _newMaterialName = string.Empty;

    [ObservableProperty]
    private string _newMaterialSpecification = string.Empty;

    [ObservableProperty]
    private string _newMaterialUnit = string.Empty;

    [ObservableProperty]
    private string _newMaterialPriceText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "请填写维修记录的必要信息。";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSearchMaterialTextChanged(string value)
    {
        FilteredMaterials.Refresh();
        UpdateMaterialSuggestionText();
    }

    partial void OnSearchRecordTextChanged(string value) => RefreshRecordFilter();

    partial void OnSearchCatalogTextChanged(string value) => FilteredCatalog.Refresh();

    partial void OnSelectedStatusFilterChanged(string value) => RefreshRecordFilter();

    partial void OnSelectedRepairPersonFilterChanged(string value) => RefreshRecordFilter();

    partial void OnSelectedRepairMethodFilterChanged(string value) => RefreshRecordFilter();

    partial void OnFilterStartDateChanged(DateTime? value) => RefreshRecordFilter();

    partial void OnFilterEndDateChanged(DateTime? value) => RefreshRecordFilter();

    partial void OnNewMaterialNameChanged(string value) => UpdateSimilarCatalogMaterials();

    public void NewRecord()
    {
        var today = DateTime.Today;
        var record = new MaintenanceRecord
        {
            OrderNumber = _database.CreateNextOrderNumber(today),
            WorkDate = today,
            CompletionDate = today,
            WorkClock = DateTime.Now.ToString("HH:mm"),
            Status = MaintenanceRecordStatus.Incomplete,
            StatusInitialized = false,
        };
        AttachDraft(record);
        _autoSaveEnabled = false;
        ClearMaterialSelections();
        StatusMessage = "新记录已创建，可挂单、保存台账或完成资料后导出。";
    }

    public MaintenanceRecord? LoadRecord(MaintenanceRecord source)
    {
        var clone = JsonSerializer.Deserialize<MaintenanceRecord>(
            JsonSerializer.Serialize(source),
            _cloneOptions);
        if (clone is null)
        {
            return null;
        }
        clone.HookCollections();
        clone.NormalizeStatus();
        AttachDraft(clone);
        _autoSaveEnabled = true;
        RefreshMaterialSelections();
        StatusMessage = $"正在查看/编辑记录 {clone.OrderNumber}。";
        SelectedPageIndex = 0;
        return clone;
    }

    public bool SaveCurrentRecord()
    {
        if (!ValidateTypedSelections())
        {
            return false;
        }

        var status = Draft.CanGenerate
            ? MaintenanceRecordStatus.Completed
            : MaintenanceRecordStatus.Incomplete;
        PersistDraft(status);
        _autoSaveEnabled = true;
        StatusMessage = status == MaintenanceRecordStatus.Completed
            ? $"记录 {Draft.OrderNumber} 已保存，资料完整，可一键导出。"
            : $"记录 {Draft.OrderNumber} 已保存，仍需补充 {Draft.MissingRequirements.Count} 项资料。";
        return true;
    }

    public bool ParkCurrentRecord()
    {
        if (!ValidateTypedSelections())
        {
            return false;
        }

        PersistDraft(MaintenanceRecordStatus.Parked);
        _autoSaveEnabled = false;
        StatusMessage = $"记录 {Draft.OrderNumber} 已挂单，可在台账中继续编辑。";
        return true;
    }

    public string? GenerateCurrentPdf(string outputPath)
    {
        if (!Draft.CanGenerate)
        {
            StatusMessage = Draft.CompletionSummary;
            return null;
        }
        if (!SaveCurrentRecord())
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? RecordExportDirectory);
        PdfReportService.Generate(outputPath, Draft);
        StatusMessage = $"维修记录已导出：{outputPath}";
        return outputPath;
    }

    public string? GeneratePdf(MaintenanceRecord record, string outputPath)
    {
        if (!record.CanGenerate)
        {
            StatusMessage = $"记录 {record.OrderNumber} {record.CompletionSummary}";
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? RecordExportDirectory);
        PdfReportService.Generate(outputPath, record);
        StatusMessage = $"维修记录已导出：{outputPath}";
        return outputPath;
    }

    public string ExportTodayReport(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? TodayExportDirectory);
        var today = DateTime.Today;
        var reportRecords = Records.ToDictionary(item => item.Id);
        if (Draft.WorkDate?.Date == today && HasMeaningfulData(Draft))
        {
            reportRecords[Draft.Id] = Draft;
        }

        var todayRecords = reportRecords.Values
            .Where(item => item.WorkDate?.Date == today)
            .OrderBy(item => item.WorkDateTimeText)
            .ToList();
        var unfinished = reportRecords.Values
            .Where(item => item.Status != MaintenanceRecordStatus.Completed)
            .OrderBy(item => item.WorkDate)
            .ThenBy(item => item.OrderNumber)
            .ToList();
        DailyReportPdfService.Generate(outputPath, today, todayRecords, unfinished);
        StatusMessage = $"今日情况已导出：{outputPath}";
        return outputPath;
    }

    public void ExportLedger(string path)
    {
        var records = FilteredRecords.Cast<MaintenanceRecord>().ToList();
        LedgerExcelService.Export(path, records);
        StatusMessage = $"已导出筛选台账，共 {records.Count} 条：{path}";
    }

    public IReadOnlyList<UsbDriveInfo> DetectUsbDrives() =>
        UsbBackupService.DetectUsbDrives();

    public void PrepareForUsbBackup()
    {
        if (HasMeaningfulData(Draft))
        {
            ValidateTypedSelections();
            var status = Draft.Status == MaintenanceRecordStatus.Parked
                ? MaintenanceRecordStatus.Parked
                : Draft.CanGenerate
                    ? MaintenanceRecordStatus.Completed
                    : MaintenanceRecordStatus.Incomplete;
            PersistDraft(status);
        }
        _database.Checkpoint();
    }

    public UsbBackupResult BackupToUsb(
        UsbDriveInfo drive,
        IProgress<UsbBackupProgress>? progress = null)
    {
        var result = UsbBackupService.Backup(DataDirectory, drive, progress);
        return result;
    }

    public string PrepareForUsbImport()
    {
        if (HasMeaningfulData(Draft))
        {
            ValidateTypedSelections();
            var status = Draft.Status == MaintenanceRecordStatus.Parked
                ? MaintenanceRecordStatus.Parked
                : Draft.CanGenerate
                    ? MaintenanceRecordStatus.Completed
                    : MaintenanceRecordStatus.Incomplete;
            PersistDraft(status);
        }

        _autoSaveTimer.Stop();
        _autoSaveEnabled = false;
        return _database.PrepareForImport();
    }

    public UsbBackupResult ImportFromUsb(
        UsbDriveInfo drive,
        IProgress<UsbBackupProgress>? progress = null) =>
        UsbBackupService.Import(DataDirectory, drive, progress);

    public void CompleteUsbImport()
    {
        _database.CompleteImport();
        _autoSaveEnabled = false;
        ReloadRecords();
        ReloadMaterials();
        ReloadOptions();
        NewRecord();
        SelectedPageIndex = 1;
        StatusMessage = "U 盘数据已导入本地并通过完整性检查。";
    }

    public void RestoreAfterFailedUsbImport(string safetyBackupPath)
    {
        _database.RestoreDatabaseBackup(safetyBackupPath);
        _autoSaveEnabled = false;
        ReloadRecords();
        ReloadMaterials();
        ReloadOptions();
        NewRecord();
        SelectedPageIndex = 1;
        StatusMessage = "U 盘导入失败，已自动恢复导入前的数据库。";
    }

    public void ClearRecordFilters()
    {
        SearchRecordText = string.Empty;
        SelectedStatusFilter = "全部状态";
        SelectedRepairPersonFilter = "全部人员";
        SelectedRepairMethodFilter = "全部方式";
        FilterStartDate = null;
        FilterEndDate = null;
        RefreshRecordFilter();
    }

    public MaterialImportResult ImportMaterials(string path)
    {
        var result = MaterialExcelService.ReadMaterials(path);
        _database.ImportMaterials(result.Materials);
        ReloadMaterials();
        RefreshMaterialSelections();
        SyncMaterialTemplate();
        StatusMessage = $"已导入 {result.Materials.Count} 条材料。";
        return result;
    }

    public void CreateMaterialTemplate(string path)
    {
        if (Materials.Count == 0)
        {
            MaterialExcelService.CreateImportTemplate(path);
        }
        else
        {
            MaterialExcelService.ExportMaterials(path, Materials);
        }
    }

    public void ExportMaterials(string path) =>
        MaterialExcelService.ExportMaterials(path, Materials);

    public void SaveMaterial(MaterialCatalogItem material)
    {
        UpsertMaterial(material);
        StatusMessage = $"材料“{material.Name}”已保存并同步到材料库模板。";
    }

    public void AddMaterialFromEditor()
    {
        if (string.IsNullOrWhiteSpace(NewMaterialName))
        {
            throw new InvalidOperationException("请输入材料名称。");
        }

        decimal? price = null;
        if (!string.IsNullOrWhiteSpace(NewMaterialPriceText))
        {
            if (!decimal.TryParse(
                    NewMaterialPriceText.Trim(),
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out var parsed) &&
                !decimal.TryParse(
                    NewMaterialPriceText.Trim(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out parsed))
            {
                throw new InvalidOperationException("材料价格格式不正确。");
            }
            price = parsed;
        }

        var material = new MaterialCatalogItem
        {
            Name = NewMaterialName.Trim(),
            Specification = NewMaterialSpecification.Trim(),
            Unit = NewMaterialUnit.Trim(),
            Price = price,
        };
        UpsertMaterial(material);
        NewMaterialName = string.Empty;
        NewMaterialSpecification = string.Empty;
        NewMaterialUnit = string.Empty;
        NewMaterialPriceText = string.Empty;
        StatusMessage = $"材料“{material.Name}”已加入材料库。";
    }

    public MaterialCatalogItem AddBlankMaterial()
    {
        var material = new MaterialCatalogItem { Name = "新材料" };
        Materials.Add(material);
        StatusMessage = "已新增材料行，请编辑名称、规格、单位和价格后保存。";
        return material;
    }

    public void DeleteMaterial(MaterialCatalogItem material)
    {
        _database.DeleteMaterial(material.Id);
        ReloadMaterials();
        RefreshMaterialSelections();
        SyncMaterialTemplate();
        StatusMessage = $"材料“{material.Name}”已删除。";
    }

    public void AddOption(string category, string value)
    {
        _database.AddOption(category, value);
        ReloadOptions();
    }

    public void RenameOption(string category, string oldValue, string newValue)
    {
        _database.RenameOption(category, oldValue, newValue);
        ReloadOptions();
    }

    public void DeleteOption(string category, string value)
    {
        _database.DeleteOption(category, value);
        ReloadOptions();
    }

    public void AddPhotos(string category, IEnumerable<string> sourcePaths)
    {
        var target = GetPhotoCollection(category);
        var directory = Path.Combine(
            GetRecordImageDirectory(Draft),
            GetPhotoCategoryName(category));
        Directory.CreateDirectory(directory);

        foreach (var sourcePath in sourcePaths)
        {
            if (!File.Exists(sourcePath) || target.Count >= 4 || target.Contains(sourcePath))
            {
                continue;
            }
            var extension = Path.GetExtension(sourcePath);
            var destination = Path.Combine(
                directory,
                $"{GetPhotoCategoryName(category)}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}");
            File.Copy(sourcePath, destination, overwrite: false);
            target.Add(destination);
        }
        OnPropertyChanged(nameof(Draft));
        QueueAutoSave();
    }

    public void RemovePhoto(string category, string path)
    {
        GetPhotoCollection(category).Remove(path);
        RunCatching(() => File.Delete(path));
        QueueAutoSave();
    }

    public void ClearPhotos(string category)
    {
        var target = GetPhotoCollection(category);
        foreach (var path in target.ToList())
        {
            RunCatching(() => File.Delete(path));
        }
        target.Clear();
        QueueAutoSave();
    }

    public void DeleteRecord(MaintenanceRecord record)
    {
        _database.DeleteRecord(record.Id);
        Records.Remove(record);
        NotifyRecordSummary();
        RefreshRecordFilter();
        StatusMessage = $"记录 {record.OrderNumber} 已删除。";
    }

    public void DeleteSelectedMaterial(RecordMaterial material)
    {
        var pick = MaterialPicks.FirstOrDefault(item =>
            item.Name == material.Name &&
            item.Specification == material.Specification &&
            item.Unit == material.Unit);
        if (pick is not null)
        {
            pick.IsSelected = false;
        }
        Draft.Materials.Remove(material);
        Draft.NotifyAll();
        QueueAutoSave();
    }

    private void AttachDraft(MaintenanceRecord record)
    {
        _suppressAutoSave = true;
        try
        {
            var previous = _draft;
            previous.PropertyChanged -= DraftOnPropertyChanged;
            previous.Materials.CollectionChanged -= MaterialsOnCollectionChanged;
            Draft = record;
            Draft.HookCollections();
            Draft.PropertyChanged += DraftOnPropertyChanged;
            Draft.Materials.CollectionChanged += MaterialsOnCollectionChanged;
            Draft.NotifyAll();
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    private void DraftOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applyingReferenceValue)
        {
            return;
        }

        if (e.PropertyName is nameof(MaintenanceRecord.CanGenerate) or
            nameof(MaintenanceRecord.CompletionSummary) or
            nameof(MaintenanceRecord.TotalCost) or
            nameof(MaintenanceRecord.MaterialCost) or
            nameof(MaintenanceRecord.Status) or
            nameof(MaintenanceRecord.StatusText) or
            nameof(MaintenanceRecord.LastSavedAt) or
            nameof(MaintenanceRecord.LastSavedText))
        {
            NotifyDraftCalculatedProperties();
        }

        ApplyReferenceValues(e.PropertyName);

        if (e.PropertyName is not nameof(MaintenanceRecord.CanGenerate) and
            not nameof(MaintenanceRecord.CompletionSummary))
        {
            QueueAutoSave();
        }
    }

    private void MaterialsOnCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        NotifyDraftCalculatedProperties();
        QueueAutoSave();
    }

    private void PersistDraft(MaintenanceRecordStatus status)
    {
        _suppressAutoSave = true;
        try
        {
            Draft.Status = status;
            Draft.StatusInitialized = true;
            Draft.LastSavedAt = DateTime.Now;
            _database.SaveRecord(Draft);
            UpsertRecord(Draft);
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    private void QueueAutoSave()
    {
        if (!_autoSaveEnabled || _suppressAutoSave)
        {
            return;
        }
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void AutoSaveTimerOnTick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        if (!_autoSaveEnabled || _suppressAutoSave)
        {
            return;
        }

        try
        {
            ValidateTypedSelections();
            var status = Draft.Status == MaintenanceRecordStatus.Parked
                ? MaintenanceRecordStatus.Parked
                : Draft.CanGenerate
                    ? MaintenanceRecordStatus.Completed
                    : MaintenanceRecordStatus.Incomplete;
            PersistDraft(status);
            StatusMessage = $"记录 {Draft.OrderNumber} 已自动保存。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"自动保存失败：{exception.Message}";
        }
    }

    private void NotifyDraftCalculatedProperties()
    {
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(MaterialCost));
    }

    private void ReloadMaterials()
    {
        Materials.Clear();
        foreach (var material in _database.LoadMaterials())
        {
            Materials.Add(material);
        }

        MaterialPicks.Clear();
        foreach (var material in Materials)
        {
            var pick = new MaterialPickItem
            {
                Material = material,
                IsSelected = Draft.Materials.Any(selected =>
                    SameMaterialKey(selected, material)),
            };
            MaterialPicks.Add(pick);
        }
        FilteredMaterials.Refresh();
        FilteredCatalog.Refresh();
        UpdateMaterialSuggestionText();
        UpdateSimilarCatalogMaterials();
        OnPropertyChanged(nameof(MaterialCount));
    }

    public void SetMaterialSelected(MaterialPickItem pick, bool selected)
    {
        pick.IsSelected = selected;

        var existing = Draft.Materials.FirstOrDefault(item =>
            SameMaterialKey(item, pick.Material));
        if (selected && existing is null)
        {
            Draft.Materials.Add(new RecordMaterial
            {
                Name = pick.Material.Name,
                Specification = pick.Material.Specification,
                Unit = pick.Material.Unit,
                UnitPrice = pick.Material.Price,
                Quantity = 1,
                Note = "仓库领用",
            });
        }
        else if (!selected && existing is not null)
        {
            Draft.Materials.Remove(existing);
        }
        Draft.NotifyAll();
        QueueAutoSave();
    }

    private void RefreshMaterialSelections()
    {
        foreach (var pick in MaterialPicks)
        {
            pick.IsSelected = Draft.Materials.Any(selected =>
                SameMaterialKey(selected, pick.Material));
        }
    }

    private void ClearMaterialSelections()
    {
        foreach (var pick in MaterialPicks)
        {
            pick.IsSelected = false;
        }
    }

    private void ReloadRecords()
    {
        Records.Clear();
        foreach (var record in _database.LoadRecords())
        {
            if (MigrateRecordPhotos(record))
            {
                _database.SaveRecord(record);
            }
            Records.Add(record);
        }
        FilteredRecords.Refresh();
        NotifyRecordSummary();
    }

    private void ReloadOptions()
    {
        Replace(RepairMethods, _database.LoadOptions(DatabaseService.RepairMethodCategory));
        Replace(RepairPeople, _database.LoadOptions(DatabaseService.RepairPersonCategory));
        Replace(LaborCompanies, _database.LoadOptions(DatabaseService.LaborCompanyCategory));
        Replace(OddJobTypes, _database.LoadOptions(DatabaseService.OddJobTypeCategory));
        Replace(MachineryTypes, _database.LoadOptions(DatabaseService.MachineryTypeCategory));
        Replace(RestorationTypes, _database.LoadOptions(DatabaseService.RestorationTypeCategory));
        RebuildFilterOptions();
    }

    private bool ValidateTypedSelections()
    {
        AddOptionIfNeeded(DatabaseService.RepairMethodCategory, Draft.RepairMethod);
        AddOptionIfNeeded(DatabaseService.RepairPersonCategory, Draft.RepairPerson);
        AddOptionIfNeeded(DatabaseService.LaborCompanyCategory, Draft.LaborCompany);
        AddOptionIfNeeded(DatabaseService.OddJobTypeCategory, Draft.OddJobType);
        AddOptionIfNeeded(DatabaseService.MachineryTypeCategory, Draft.MachineryType);
        AddOptionIfNeeded(DatabaseService.RestorationTypeCategory, Draft.RestorationType);
        return true;
    }

    private void AddOptionIfNeeded(string category, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        _database.AddOption(category, value);
        var target = category switch
        {
            DatabaseService.RepairMethodCategory => RepairMethods,
            DatabaseService.RepairPersonCategory => RepairPeople,
            DatabaseService.LaborCompanyCategory => LaborCompanies,
            DatabaseService.OddJobTypeCategory => OddJobTypes,
            DatabaseService.MachineryTypeCategory => MachineryTypes,
            DatabaseService.RestorationTypeCategory => RestorationTypes,
            _ => null,
        };
        if (target is not null && !target.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(value);
        }
    }

    private void UpsertRecord(MaintenanceRecord record)
    {
        var existing = Records.FirstOrDefault(item => item.Id == record.Id);
        if (existing is null)
        {
            Records.Insert(0, record);
        }
        else
        {
            var index = Records.IndexOf(existing);
            Records[index] = record;
        }
        FilteredRecords.Refresh();
        NotifyRecordSummary();
        SelectedRecord = record;
    }

    private void NotifyRecordSummary()
    {
        OnPropertyChanged(nameof(RecordCount));
        OnPropertyChanged(nameof(TodayCount));
        OnPropertyChanged(nameof(UnfinishedCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(MatchedRecordCount));
    }

    private bool FilterMaterial(object item)
    {
        if (item is not MaterialPickItem material)
        {
            return false;
        }
        var keyword = SearchMaterialText.Trim();
        return keyword.Length == 0 ||
               material.Material.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterRecord(object item)
    {
        if (item is not MaintenanceRecord record)
        {
            return false;
        }

        var keyword = SearchRecordText.Trim();
        if (keyword.Length > 0 &&
            !record.OrderNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
            !record.ExternalOrderNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
            !record.Location.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
            !record.RepairPerson.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
            !record.RepairReason.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var date = record.WorkDate?.Date;
        if (FilterStartDate.HasValue && (date is null || date < FilterStartDate.Value.Date))
        {
            return false;
        }
        if (FilterEndDate.HasValue && (date is null || date > FilterEndDate.Value.Date))
        {
            return false;
        }
        if (SelectedRepairPersonFilter != "全部人员" &&
            !string.Equals(record.RepairPerson, SelectedRepairPersonFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (SelectedRepairMethodFilter != "全部方式" &&
            !string.Equals(record.RepairMethod, SelectedRepairMethodFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SelectedStatusFilter switch
        {
            "已完成" => record.Status == MaintenanceRecordStatus.Completed,
            "未完成" => record.Status == MaintenanceRecordStatus.Incomplete,
            "挂单" => record.Status == MaintenanceRecordStatus.Parked,
            _ => true,
        };
    }

    private bool FilterCatalogMaterial(object item)
    {
        if (item is not MaterialCatalogItem material)
        {
            return false;
        }
        var keyword = SearchCatalogText.Trim();
        return keyword.Length == 0 ||
               material.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private ObservableCollection<string> GetPhotoCollection(string category) =>
        category switch
        {
            "Before" => Draft.BeforePhotos,
            "During" => Draft.DuringPhotos,
            "After" => Draft.AfterPhotos,
            "Machinery" => Draft.MachineryPhotos,
            "Restoration" => Draft.RestorationPhotos,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private string GetRecordImageDirectory(MaintenanceRecord record)
    {
        var folderName = string.IsNullOrWhiteSpace(record.OrderNumber)
            ? record.Id
            : record.OrderNumber;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            folderName = folderName.Replace(invalid, '-');
        }
        return Path.Combine(ImagesDirectory, folderName);
    }

    private static string GetPhotoCategoryName(string category) =>
        category switch
        {
            "Before" => "维修前",
            "During" => "施工现场",
            "After" => "维修后",
            "Machinery" => "机械使用",
            "Restoration" => "还原路面",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private bool MigrateRecordPhotos(MaintenanceRecord record)
    {
        var changed = false;
        changed |= MigratePhotoCollection(record, record.BeforePhotos, "维修前");
        changed |= MigratePhotoCollection(record, record.DuringPhotos, "施工现场");
        changed |= MigratePhotoCollection(record, record.AfterPhotos, "维修后");
        changed |= MigratePhotoCollection(record, record.MachineryPhotos, "机械使用");
        changed |= MigratePhotoCollection(record, record.RestorationPhotos, "还原路面");
        return changed;
    }

    private bool MigratePhotoCollection(
        MaintenanceRecord record,
        ObservableCollection<string> photos,
        string categoryName)
    {
        var changed = false;
        var targetDirectory = Path.Combine(GetRecordImageDirectory(record), categoryName);
        for (var index = 0; index < photos.Count; index++)
        {
            var sourcePath = photos[index];
            var rebasedPath = RebaseManagedPhotoPath(sourcePath);
            if (rebasedPath is not null && File.Exists(rebasedPath))
            {
                photos[index] = rebasedPath;
                changed = true;
                continue;
            }
            if (!File.Exists(sourcePath) || IsInsideDirectory(sourcePath, ImagesDirectory))
            {
                continue;
            }

            Directory.CreateDirectory(targetDirectory);
            var destination = Path.Combine(
                targetDirectory,
                $"{categoryName}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
            try
            {
                File.Copy(sourcePath, destination, overwrite: false);
                photos[index] = destination;
                changed = true;
            }
            catch
            {
                // Keep the original image if migration is unavailable.
            }
        }
        return changed;
    }

    private string? RebaseManagedPhotoPath(string sourcePath)
    {
        try
        {
            var normalized = Path.GetFullPath(sourcePath);
            var marker = $"{Path.DirectorySeparatorChar}管网维修数据" +
                         $"{Path.DirectorySeparatorChar}图片" +
                         Path.DirectorySeparatorChar;
            var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var relativePath = normalized[(markerIndex + marker.Length)..];
            return Path.Combine(ImagesDirectory, relativePath);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsInsideDirectory(string filePath, string directory)
    {
        try
        {
            var fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(filePath);
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void UpsertMaterial(MaterialCatalogItem material)
    {
        var matching = Materials.FirstOrDefault(item =>
            item.Id != material.Id &&
            SameMaterialKey(material, item));
        if (matching is not null)
        {
            matching.Name = material.Name.Trim();
            matching.Specification = material.Specification.Trim();
            matching.Unit = material.Unit.Trim();
            if (material.Price.HasValue)
            {
                matching.Price = material.Price;
            }
            _database.SaveMaterial(matching);
        }
        else
        {
            material.Name = material.Name.Trim();
            material.Specification = material.Specification.Trim();
            material.Unit = material.Unit.Trim();
            _database.SaveMaterial(material);
        }

        ReloadMaterials();
        RefreshMaterialSelections();
        SyncMaterialTemplate();
    }

    private void SyncMaterialTemplate()
    {
        try
        {
            Directory.CreateDirectory(LibraryDirectory);
            MaterialExcelService.ExportMaterials(MaterialTemplatePath, Materials);
        }
        catch (Exception exception)
        {
            StatusMessage = $"材料已保存，但模板同步失败：{exception.Message}";
        }
    }

    private void UpdateMaterialSuggestionText()
    {
        var keyword = SearchMaterialText.Trim();
        if (keyword.Length == 0)
        {
            MaterialSuggestionText = string.Empty;
            return;
        }

        var suggestions = FilteredMaterials
            .Cast<MaterialPickItem>()
            .Take(4)
            .Select(item => item.DisplayText.Trim())
            .ToList();
        MaterialSuggestionText = suggestions.Count == 0
            ? "材料库中暂无相似材料，可在“材料库”中新增。"
            : $"相似材料：{string.Join("  |  ", suggestions)}";
    }

    private void UpdateSimilarCatalogMaterials()
    {
        SimilarCatalogMaterials.Clear();
        var keyword = NewMaterialName.Trim();
        if (keyword.Length > 0)
        {
            foreach (var material in Materials
                         .Where(item =>
                             item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                             keyword.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(item => item.Name)
                         .Take(6))
            {
                SimilarCatalogMaterials.Add(material);
            }
        }
        OnPropertyChanged(nameof(HasSimilarCatalogMaterials));
    }

    private void RebuildFilterOptions()
    {
        var person = SelectedRepairPersonFilter;
        var method = SelectedRepairMethodFilter;

        RepairPersonFilters.Clear();
        RepairPersonFilters.Add("全部人员");
        foreach (var value in RepairPeople)
        {
            RepairPersonFilters.Add(value);
        }
        SelectedRepairPersonFilter = RepairPersonFilters.Contains(person)
            ? person
            : "全部人员";

        RepairMethodFilters.Clear();
        RepairMethodFilters.Add("全部方式");
        foreach (var value in RepairMethods)
        {
            RepairMethodFilters.Add(value);
        }
        SelectedRepairMethodFilter = RepairMethodFilters.Contains(method)
            ? method
            : "全部方式";
    }

    private void RefreshRecordFilter()
    {
        FilteredRecords.Refresh();
        OnPropertyChanged(nameof(MatchedRecordCount));
    }

    private void ApplyReferenceValues(string? propertyName)
    {
        _applyingReferenceValue = true;
        try
        {
            if (propertyName == nameof(MaintenanceRecord.OddJobType) &&
                string.IsNullOrWhiteSpace(Draft.OddJobRateText) &&
                OddJobReferenceRates.TryGetValue(Draft.OddJobType.Trim(), out var oddRate))
            {
                Draft.OddJobRateText = oddRate.ToString(CultureInfo.InvariantCulture);
            }

            if (propertyName == nameof(MaintenanceRecord.MachineryType) &&
                string.IsNullOrWhiteSpace(Draft.MachineryRateText) &&
                MachineryReferenceRates.TryGetValue(Draft.MachineryType.Trim(), out var machineryRate))
            {
                Draft.MachineryRateText = machineryRate.ToString(CultureInfo.InvariantCulture);
            }

            if (propertyName == nameof(MaintenanceRecord.RestorationType) &&
                string.IsNullOrWhiteSpace(Draft.RestorationUnitPriceText) &&
                RestorationReferenceRates.TryGetValue(Draft.RestorationType.Trim(), out var restorationRate))
            {
                Draft.RestorationUnitPriceText = restorationRate.ToString(CultureInfo.InvariantCulture);
            }

            if (propertyName is nameof(MaintenanceRecord.OddJobType) or
                nameof(MaintenanceRecord.OddJobRateText) or
                nameof(MaintenanceRecord.OddJobTime) or
                nameof(MaintenanceRecord.OddJobPeople))
            {
                UpdateOddJobCostIfEmpty();
            }

            if (propertyName is nameof(MaintenanceRecord.MachineryType) or
                nameof(MaintenanceRecord.MachineryRateText) or
                nameof(MaintenanceRecord.MachineryTime))
            {
                UpdateMachineryCostIfEmpty();
            }

            if (propertyName is nameof(MaintenanceRecord.RestorationType) or
                nameof(MaintenanceRecord.RestorationUnitPriceText) or
                nameof(MaintenanceRecord.RestorationAreaText))
            {
                UpdateRestorationCostIfEmpty();
            }
        }
        finally
        {
            _applyingReferenceValue = false;
        }
    }

    private void UpdateOddJobCostIfEmpty()
    {
        if (!Draft.HasOddJob ||
            !string.IsNullOrWhiteSpace(Draft.OddJobCostText) ||
            !TryParseNumber(Draft.OddJobRateText, out var rate) ||
            !TryParseNumber(Draft.OddJobTime, out var hours))
        {
            return;
        }
        var people = Math.Max(1, Draft.OddJobPeople);
        Draft.OddJobCostText = (people * rate * hours).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void UpdateMachineryCostIfEmpty()
    {
        if (!Draft.HasMachinery ||
            !string.IsNullOrWhiteSpace(Draft.MachineryCostText) ||
            !TryParseNumber(Draft.MachineryRateText, out var rate) ||
            !TryParseNumber(Draft.MachineryTime, out var hours))
        {
            return;
        }
        Draft.MachineryCostText = (rate * hours).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void UpdateRestorationCostIfEmpty()
    {
        if (!Draft.Restored ||
            !string.IsNullOrWhiteSpace(Draft.RestorationCostText) ||
            !TryParseNumber(Draft.RestorationUnitPriceText, out var unitPrice) ||
            !TryParseNumber(Draft.RestorationAreaText, out var area))
        {
            return;
        }
        Draft.RestorationCostText = (unitPrice * area).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool TryParseNumber(string text, out decimal value)
    {
        value = 0;
        var match = Regex.Match(text ?? string.Empty, @"\d+(?:\.\d+)?");
        return match.Success &&
               decimal.TryParse(
                   match.Value,
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private static bool HasMeaningfulData(MaintenanceRecord record) =>
        !string.IsNullOrWhiteSpace(record.Location) ||
        !string.IsNullOrWhiteSpace(record.RepairPerson) ||
        !string.IsNullOrWhiteSpace(record.RepairReason) ||
        record.Materials.Count > 0 ||
        record.BeforePhotos.Count > 0 ||
        record.AfterPhotos.Count > 0;

    private static bool SameMaterialKey(RecordMaterial left, MaterialCatalogItem right) =>
        string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Specification.Trim(), right.Specification.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Unit.Trim(), right.Unit.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameMaterialKey(MaterialCatalogItem left, MaterialCatalogItem right) =>
        string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Specification.Trim(), right.Specification.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Unit.Trim(), right.Unit.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static void RunCatching(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // A missing image file should not prevent the record from being edited.
        }
    }
}
