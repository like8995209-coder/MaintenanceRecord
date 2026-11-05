using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MaintenanceRecordApp.Models;

public partial class MaintenanceRecord : ObservableObject
{
    private static readonly CultureInfo CostCulture = CultureInfo.InvariantCulture;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _orderNumber = string.Empty;

    [ObservableProperty]
    private string _externalOrderNumber = string.Empty;

    [ObservableProperty]
    private DateTime? _workDate = DateTime.Today;

    [ObservableProperty]
    private string _workClock = DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    private DateTime? _completionDate = DateTime.Today;

    [ObservableProperty]
    private string _completionClock = string.Empty;

    [ObservableProperty]
    private string _source = "客服转单";

    [ObservableProperty]
    private string _repairPerson = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private string _repairReason = string.Empty;

    [ObservableProperty]
    private string _contact = string.Empty;

    [ObservableProperty]
    private string _repairMethod = string.Empty;

    [ObservableProperty]
    private MaintenanceRecordStatus _status = MaintenanceRecordStatus.Incomplete;

    [ObservableProperty]
    private bool _statusInitialized;

    [ObservableProperty]
    private DateTime? _lastSavedAt;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _hasOddJob;

    [ObservableProperty]
    private string _oddJobTime = string.Empty;

    [ObservableProperty]
    private string _oddJobType = string.Empty;

    [ObservableProperty]
    private int _oddJobPeople = 1;

    [ObservableProperty]
    private string _oddJobRateText = string.Empty;

    [ObservableProperty]
    private string _laborCompany = string.Empty;

    [ObservableProperty]
    private string _oddJobCostText = string.Empty;

    [ObservableProperty]
    private bool _hasMachinery;

    [ObservableProperty]
    private string _machineryTime = string.Empty;

    [ObservableProperty]
    private string _machineryType = string.Empty;

    [ObservableProperty]
    private string _machineryRateText = string.Empty;

    [ObservableProperty]
    private string _machineryCostText = string.Empty;

    [ObservableProperty]
    private bool _restored;

    [ObservableProperty]
    private string _restorationCostText = string.Empty;

    [ObservableProperty]
    private string _restorationType = string.Empty;

    [ObservableProperty]
    private string _restorationUnitPriceText = string.Empty;

    [ObservableProperty]
    private string _restorationAreaText = string.Empty;

    [ObservableProperty]
    private bool? _accepted;

    [ObservableProperty]
    private string _preparedBy = string.Empty;

    [ObservableProperty]
    private string _reviewDepartment = "管网部";

    [ObservableProperty]
    private string _responsiblePerson = string.Empty;

    public ObservableCollection<RecordMaterial> Materials { get; set; } = [];

    public ObservableCollection<string> BeforePhotos { get; set; } = [];

    public ObservableCollection<string> DuringPhotos { get; set; } = [];

    public ObservableCollection<string> AfterPhotos { get; set; } = [];

    public ObservableCollection<string> MachineryPhotos { get; set; } = [];

    public ObservableCollection<string> RestorationPhotos { get; set; } = [];

    [JsonIgnore]
    public string WorkDateTimeText => FormatDateTime(WorkDate, WorkClock);

    [JsonIgnore]
    public string CompletionDateTimeText => FormatDateTime(CompletionDate, CompletionClock);

    [JsonIgnore]
    public decimal MaterialCost => Materials.Sum(item => item.Subtotal);

    [JsonIgnore]
    public decimal OddJobCost => HasOddJob ? ParseCost(OddJobCostText) ?? 0 : 0;

    [JsonIgnore]
    public decimal MachineryCost => HasMachinery ? ParseCost(MachineryCostText) ?? 0 : 0;

    [JsonIgnore]
    public decimal RestorationCost => Restored ? ParseCost(RestorationCostText) ?? 0 : 0;

    [JsonIgnore]
    public decimal TotalCost => OddJobCost + MachineryCost + MaterialCost + RestorationCost;

    [JsonIgnore]
    public string StatusText =>
        Status switch
        {
            MaintenanceRecordStatus.Parked => "挂单",
            MaintenanceRecordStatus.Incomplete => "未完成",
            _ => "已完成",
        };

    [JsonIgnore]
    public string LastSavedText =>
        LastSavedAt is null ? "尚未保存" : LastSavedAt.Value.ToString("MM-dd HH:mm");

    [JsonIgnore]
    public IReadOnlyList<string> MissingRequirements
    {
        get
        {
            var missing = new List<string>();
            AddMissing(missing, string.IsNullOrWhiteSpace(Source), "单据来源");
            AddMissing(missing, string.IsNullOrWhiteSpace(RepairPerson), "维修人员");
            AddMissing(missing, string.IsNullOrWhiteSpace(Location), "维修地点");
            AddMissing(missing, string.IsNullOrWhiteSpace(RepairReason), "维修原因");
            AddMissing(missing, string.IsNullOrWhiteSpace(RepairMethod), "维修方式");
            AddMissing(missing, WorkDate is null, "维修日期");
            AddMissing(missing, string.IsNullOrWhiteSpace(WorkClock), "维修时间");
            AddMissing(missing, CompletionDate is null, "竣工日期");
            AddMissing(missing, string.IsNullOrWhiteSpace(CompletionClock), "竣工时间");
            AddMissing(missing, Materials.Count == 0, "材料清单");
            AddMissing(missing, !Accepted.HasValue, "验收结果");
            AddMissing(missing, string.IsNullOrWhiteSpace(PreparedBy), "制单人");
            AddMissing(
                missing,
                HasOddJob && string.IsNullOrWhiteSpace(OddJobTime),
                "杂工时间");
            AddMissing(
                missing,
                HasOddJob && string.IsNullOrWhiteSpace(LaborCompany),
                "劳务公司");
            AddMissing(
                missing,
                HasOddJob && string.IsNullOrWhiteSpace(OddJobCostText),
                "杂工费用");
            AddMissing(
                missing,
                HasMachinery && string.IsNullOrWhiteSpace(MachineryTime),
                "机械时间");
            AddMissing(
                missing,
                HasMachinery && string.IsNullOrWhiteSpace(MachineryCostText),
                "机械费用");
            AddMissing(
                missing,
                Restored && string.IsNullOrWhiteSpace(RestorationCostText),
                "还原费用");
            return missing;
        }
    }

    [JsonIgnore]
    public bool CanGenerate => MissingRequirements.Count == 0;

    [JsonIgnore]
    public double CompletionPercent =>
        Math.Round((12 - MissingRequirements.Count) / 12d * 100, 0);

    [JsonIgnore]
    public bool HasOddJobNo
    {
        get => !HasOddJob;
        set
        {
            if (value)
            {
                HasOddJob = false;
            }
        }
    }

    [JsonIgnore]
    public bool HasMachineryNo
    {
        get => !HasMachinery;
        set
        {
            if (value)
            {
                HasMachinery = false;
            }
        }
    }

    [JsonIgnore]
    public bool RestoredNo
    {
        get => !Restored;
        set
        {
            if (value)
            {
                Restored = false;
            }
        }
    }

    [JsonIgnore]
    public bool AcceptedYes
    {
        get => Accepted == true;
        set
        {
            if (value)
            {
                Accepted = true;
            }
        }
    }

    [JsonIgnore]
    public bool AcceptedNo
    {
        get => Accepted == false;
        set
        {
            if (value)
            {
                Accepted = false;
            }
        }
    }

    [JsonIgnore]
    public string CompletionSummary =>
        CanGenerate
            ? "必要信息已填写完整"
            : $"还需填写：{string.Join("、", MissingRequirements)}";

    [JsonIgnore]
    public string AcceptanceText =>
        Accepted switch
        {
            true => "是",
            false => "否",
            _ => "未填",
        };

    public MaintenanceRecord()
    {
        HookCollections();
    }

    public void HookCollections()
    {
        Materials ??= [];
        BeforePhotos ??= [];
        DuringPhotos ??= [];
        AfterPhotos ??= [];
        MachineryPhotos ??= [];
        RestorationPhotos ??= [];

        Materials.CollectionChanged -= MaterialsOnCollectionChanged;
        Materials.CollectionChanged += MaterialsOnCollectionChanged;
        foreach (var material in Materials)
        {
            material.PropertyChanged -= MaterialOnPropertyChanged;
            material.PropertyChanged += MaterialOnPropertyChanged;
        }
        NotifyCalculatedProperties();
    }

    public void NotifyAll()
    {
        NotifyCalculatedProperties();
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(CompletionSummary));
    }

    partial void OnWorkDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(WorkDateTimeText));
        NotifyAll();
    }

    partial void OnWorkClockChanged(string value)
    {
        OnPropertyChanged(nameof(WorkDateTimeText));
        NotifyAll();
    }

    partial void OnCompletionDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(CompletionDateTimeText));
        NotifyAll();
    }

    partial void OnCompletionClockChanged(string value)
    {
        OnPropertyChanged(nameof(CompletionDateTimeText));
        NotifyAll();
    }

    partial void OnSourceChanged(string value) => NotifyAll();

    partial void OnRepairPersonChanged(string value) => NotifyAll();

    partial void OnLocationChanged(string value) => NotifyAll();

    partial void OnRepairReasonChanged(string value) => NotifyAll();

    partial void OnRepairMethodChanged(string value) => NotifyAll();

    partial void OnStatusChanged(MaintenanceRecordStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        NotifyAll();
    }

    partial void OnLastSavedAtChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(LastSavedText));
    }

    partial void OnPreparedByChanged(string value) => NotifyAll();

    partial void OnAcceptedChanged(bool? value)
    {
        OnPropertyChanged(nameof(AcceptedYes));
        OnPropertyChanged(nameof(AcceptedNo));
        OnPropertyChanged(nameof(AcceptanceText));
        NotifyAll();
    }

    partial void OnHasOddJobChanged(bool value)
    {
        if (!value)
        {
            OddJobTime = string.Empty;
            OddJobType = string.Empty;
            OddJobPeople = 1;
            OddJobRateText = string.Empty;
            LaborCompany = string.Empty;
            OddJobCostText = string.Empty;
        }
        OnPropertyChanged(nameof(HasOddJobNo));
        NotifyCalculatedProperties();
    }

    partial void OnHasMachineryChanged(bool value)
    {
        if (!value)
        {
            MachineryTime = string.Empty;
            MachineryType = string.Empty;
            MachineryRateText = string.Empty;
            MachineryCostText = string.Empty;
        }
        OnPropertyChanged(nameof(HasMachineryNo));
        NotifyCalculatedProperties();
    }

    partial void OnRestoredChanged(bool value)
    {
        if (!value)
        {
            RestorationCostText = string.Empty;
            RestorationType = string.Empty;
            RestorationUnitPriceText = string.Empty;
            RestorationAreaText = string.Empty;
        }
        OnPropertyChanged(nameof(RestoredNo));
        NotifyCalculatedProperties();
    }

    partial void OnOddJobTimeChanged(string value) => NotifyAll();

    partial void OnOddJobTypeChanged(string value) => NotifyAll();

    partial void OnOddJobPeopleChanged(int value) => NotifyAll();

    partial void OnOddJobRateTextChanged(string value) => NotifyAll();

    partial void OnLaborCompanyChanged(string value) => NotifyAll();

    partial void OnOddJobCostTextChanged(string value) => NotifyAll();

    partial void OnMachineryTimeChanged(string value) => NotifyAll();

    partial void OnMachineryTypeChanged(string value) => NotifyAll();

    partial void OnMachineryRateTextChanged(string value) => NotifyAll();

    partial void OnMachineryCostTextChanged(string value) => NotifyAll();

    partial void OnRestorationTypeChanged(string value) => NotifyAll();

    partial void OnRestorationUnitPriceTextChanged(string value) => NotifyAll();

    partial void OnRestorationAreaTextChanged(string value) => NotifyAll();

    partial void OnRestorationCostTextChanged(string value) => NotifyAll();

    private void MaterialsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RecordMaterial item in e.OldItems)
            {
                item.PropertyChanged -= MaterialOnPropertyChanged;
            }
        }
        if (e.NewItems is not null)
        {
            foreach (RecordMaterial item in e.NewItems)
            {
                item.PropertyChanged += MaterialOnPropertyChanged;
            }
        }
        NotifyCalculatedProperties();
    }

    private void MaterialOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordMaterial.Quantity) or
            nameof(RecordMaterial.UnitPrice) or
            nameof(RecordMaterial.Name) or
            nameof(RecordMaterial.Specification))
        {
            NotifyCalculatedProperties();
        }
    }

    private void NotifyCalculatedProperties()
    {
        OnPropertyChanged(nameof(MaterialCost));
        OnPropertyChanged(nameof(OddJobCost));
        OnPropertyChanged(nameof(MachineryCost));
        OnPropertyChanged(nameof(RestorationCost));
        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(CompletionPercent));
        OnPropertyChanged(nameof(CompletionSummary));
    }

    public void NormalizeStatus()
    {
        if (StatusInitialized)
        {
            return;
        }

        Status = CanGenerate
            ? MaintenanceRecordStatus.Completed
            : MaintenanceRecordStatus.Incomplete;
    }

    private static void AddMissing(List<string> missing, bool condition, string label)
    {
        if (condition)
        {
            missing.Add(label);
        }
    }

    private static decimal? ParseCost(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return decimal.TryParse(value, NumberStyles.Number, CostCulture, out var parsed) ||
               decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed)
            ? parsed
            : null;
    }

    private static string FormatDateTime(DateTime? date, string clock)
    {
        if (date is null)
        {
            return string.Empty;
        }
        var normalizedClock = NormalizeClock(clock);
        return $"{date.Value:yyyy.MM.dd} {normalizedClock}".Trim();
    }

    private static string NormalizeClock(string value)
    {
        var text = (value ?? string.Empty)
            .Trim()
            .Replace("时", string.Empty)
            .Replace("点", string.Empty)
            .Replace("分", string.Empty);
        if (text.Length == 0)
        {
            return string.Empty;
        }
        if (text.All(char.IsDigit))
        {
            var hour = text.Length switch
            {
                1 or 2 => int.TryParse(text, out var parsedHour) ? parsedHour : -1,
                3 => int.TryParse(text[..1], out var h1) && int.TryParse(text[1..], out var m1)
                    ? h1 * 60 + m1
                    : -1,
                4 => int.TryParse(text[..2], out var h2) && int.TryParse(text[2..], out var m2)
                    ? h2 * 60 + m2
                    : -1,
                _ => -1,
            };
            if (hour >= 0 && hour <= 23)
            {
                return $"{hour:00}:00";
            }
            if (hour >= 0 && hour < 24 * 60)
            {
                return $"{hour / 60:00}:{hour % 60:00}";
            }
        }
        return TimeOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("HH:mm", CultureInfo.InvariantCulture)
            : text;
    }

    public RecordMaterial[] PricedMaterials() =>
        Materials.Where(item => item.UnitPrice.HasValue).ToArray();
}
