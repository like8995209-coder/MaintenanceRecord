using System.IO;
using ClosedXML.Excel;
using MaintenanceRecordApp.Models;
using MaintenanceRecordApp.Services;
using PdfSharp.Pdf.IO;

namespace MaintenanceRecordTests;

public sealed class MaintenanceRecordTests
{
    private static readonly string ArtifactDirectory =
        Path.Combine(Path.GetTempPath(), "MaintenanceRecordTests", "artifacts");

    [Fact]
    public void MaterialWorkbookIsRecognizedAndHashedForDuplicateRemoval()
    {
        Directory.CreateDirectory(ArtifactDirectory);
        var inputPath = Path.Combine(ArtifactDirectory, "materials-input.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("材料库");
            var values = new object[,]
            {
                { "材料名称", "规格型号", "单位", "价格" },
                { "铜加密阀", "DN20", "个", 120.5m },
                { "哈夫节", "DN200", "套", "380.00" },
                { "铜加密阀", "DN20", "个", 999m },
            };
            for (var row = 0; row < values.GetLength(0); row++)
            {
                for (var column = 0; column < values.GetLength(1); column++)
                {
                    var value = values[row, column];
                    if (value is decimal number)
                    {
                        sheet.Cell(row + 1, column + 1).Value = number;
                    }
                    else
                    {
                        sheet.Cell(row + 1, column + 1).Value = value?.ToString() ?? string.Empty;
                    }
                }
            }
            workbook.SaveAs(inputPath);
        }

        var result = MaterialExcelService.ReadMaterials(inputPath);

        Assert.Equal(2, result.Materials.Count);
        Assert.Equal(120.5m, result.Materials[0].Price);
        Assert.Equal("DN200", result.Materials[1].Specification);
        Assert.Equal(380m, result.Materials[1].Price);
    }

    [Fact]
    public void DatabasePersistsRecordsAndIncrementsDailySequence()
    {
        var dataDirectory = Path.Combine(ArtifactDirectory, $"db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        var database = new DatabaseService(dataDirectory);
        Assert.True(Directory.Exists(Path.Combine(dataDirectory, "今日")));
        Assert.True(Directory.Exists(Path.Combine(dataDirectory, "维修记录")));
        Assert.True(Directory.Exists(Path.Combine(dataDirectory, "库")));
        Assert.True(Directory.Exists(Path.Combine(dataDirectory, "图片")));

        var firstNumber = database.CreateNextOrderNumber(new DateTime(2026, 11, 4));
        Assert.Equal("JLYL-2026-GWB-1104-01", firstNumber);

        var record = CreateCompleteRecord(firstNumber);
        database.SaveRecord(record);

        Assert.Equal(
            "JLYL-2026-GWB-1104-02",
            database.CreateNextOrderNumber(new DateTime(2026, 11, 4)));
        var loaded = Assert.Single(database.LoadRecords());
        Assert.Equal(firstNumber, loaded.OrderNumber);
        Assert.Single(loaded.Materials);
        Assert.True(loaded.CanGenerate);
        Assert.Equal(MaintenanceRecordStatus.Completed, loaded.Status);
        var safetyBackup = database.PrepareForImport();
        Assert.True(File.Exists(safetyBackup));
        database.CompleteImport();
        Assert.Equal(firstNumber, Assert.Single(database.LoadRecords()).OrderNumber);
    }

    [Fact]
    public void RequiredFieldsAndOptionalSectionsDriveCompletion()
    {
        var record = new MaintenanceRecord();
        Assert.False(record.CanGenerate);
        Assert.Contains("维修人员", record.MissingRequirements);
        Assert.Contains("材料清单", record.MissingRequirements);

        record.Source = "热线";
        record.RepairPerson = "李华";
        record.Location = "建设小区";
        record.RepairReason = "阀门渗漏";
        record.RepairMethod = "更换阀门";
        record.WorkDate = new DateTime(2026, 11, 4);
        record.WorkClock = "09:00";
        record.CompletionDate = new DateTime(2026, 11, 4);
        record.CompletionClock = "16";
        record.Accepted = true;
        record.PreparedBy = "李慧";
        record.Materials.Add(new RecordMaterial
        {
            Name = "铜加密阀",
            Unit = "个",
            Quantity = 2,
            UnitPrice = 60m,
        });

        Assert.True(record.CanGenerate);
        Assert.Equal(120m, record.TotalCost);
        Assert.Equal("2026.11.04 16:00", record.CompletionDateTimeText);

        record.HasMachinery = true;
        record.MachineryCostText = "80";
        Assert.Equal(200m, record.TotalCost);
        record.HasMachinery = false;
        Assert.Equal(120m, record.TotalCost);
        Assert.Equal(string.Empty, record.MachineryCostText);
    }

    [Fact]
    public void PdfAndExcelOutputsAreGenerated()
    {
        Directory.CreateDirectory(ArtifactDirectory);
        var imagePath = Path.Combine(ArtifactDirectory, "sample-photo.png");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAB4AAAAyCAYAAABLXmvvAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAABkSURBVFhH7dKxCcAwDAXRTJQhPY23dED1B2MQFxf34bW6Rs87x/qDYYxhTAx3LzUMl+6lxl3PRTCMMVy6lxqGS/dS467nIhjGGC6nSzd2DJfTpRs7dz0XwTDGMMYwxjDGMGSsD6bqDLb+sPmsAAAAAElFTkSuQmCC"
            )
        );

        var record = CreateCompleteRecord("JLYL-2026-GWB-1104-01");
        record.BeforePhotos.Add(imagePath);
        record.AfterPhotos.Add(imagePath);
        record.DuringPhotos.Add(imagePath);
        record.MachineryPhotos.Add(imagePath);
        record.RestorationPhotos.Add(imagePath);

        var pdfPath = Path.Combine(ArtifactDirectory, "维修记录-测试.pdf");
        var excelPath = Path.Combine(ArtifactDirectory, "台账-测试.xlsx");
        PdfReportService.Generate(pdfPath, record);
        LedgerExcelService.Export(excelPath, [record]);
        MaterialExcelService.CreateImportTemplate(
            Path.Combine(ArtifactDirectory, "material-template.xlsx"));

        Assert.True(new FileInfo(pdfPath).Length > 5_000);
        using (var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import))
        {
            Assert.Equal(1, pdf.PageCount);
            Assert.InRange(pdf.Pages[0].Width.Point, 841.8, 842.0);
            Assert.InRange(pdf.Pages[0].Height.Point, 595.2, 595.4);
        }
        Assert.True(new FileInfo(excelPath).Length > 3_000);
        using (var workbook = new XLWorkbook(excelPath))
        {
            var sheet = workbook.Worksheet("维修台账");
            Assert.Equal("编号", sheet.Cell(3, 1).GetString());
            Assert.Equal("材料名称", sheet.Cell(3, 16).GetString());
            Assert.Equal("维修成本合计", sheet.Cell(3, 22).GetString());
            Assert.Equal("维修前图", sheet.Cell(3, 23).GetString());
            Assert.Equal("维修后图", sheet.Cell(3, 24).GetString());
            Assert.Equal("机械使用图", sheet.Cell(3, 25).GetString());
            Assert.Equal("还原路面图", sheet.Cell(3, 26).GetString());
            Assert.True(sheet.Cell(4, 23).HasHyperlink);
            Assert.True(File.Exists(sheet.Cell(4, 23).GetHyperlink().ExternalAddress.LocalPath));

            var imageSheet = workbook.Worksheet("图片清单");
            Assert.Equal("工单编号", imageSheet.Cell(1, 1).GetString());
            Assert.Equal(5, imageSheet.LastRowUsed()!.RowNumber() - 1);
            Assert.True(imageSheet.Cell(2, 4).HasHyperlink);
        }
        Assert.True(
            new FileInfo(Path.Combine(ArtifactDirectory, "material-template.xlsx")).Length > 3_000);
    }

    [Fact]
    public void DailyReportUsesPortraitA4()
    {
        Directory.CreateDirectory(ArtifactDirectory);
        var completed = CreateCompleteRecord("JLYL-2026-GWB-1105-01");
        var parked = CreateCompleteRecord("JLYL-2026-GWB-1105-02");
        parked.Status = MaintenanceRecordStatus.Parked;
        parked.StatusInitialized = true;
        parked.CompletionClock = string.Empty;

        var pdfPath = Path.Combine(ArtifactDirectory, "今日情况-测试.pdf");
        DailyReportPdfService.Generate(
            pdfPath,
            new DateTime(2026, 11, 5),
            [completed, parked],
            [parked]);

        Assert.True(new FileInfo(pdfPath).Length > 3_000);
        using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Equal(1, pdf.PageCount);
        Assert.InRange(pdf.Pages[0].Width.Point, 595.2, 595.4);
        Assert.InRange(pdf.Pages[0].Height.Point, 841.8, 842.0);
    }

    [Fact]
    public void DailyReportKeepsLargeDailyListsOnOnePortraitPage()
    {
        Directory.CreateDirectory(ArtifactDirectory);
        var records = Enumerable.Range(1, 20)
            .Select(index =>
            {
                var record = CreateCompleteRecord($"JLYL-2026-GWB-1105-{index:00}");
                record.Status = index <= 10
                    ? MaintenanceRecordStatus.Completed
                    : MaintenanceRecordStatus.Parked;
                return record;
            })
            .ToList();

        var pdfPath = Path.Combine(ArtifactDirectory, "今日情况-多记录.pdf");
        DailyReportPdfService.Generate(
            pdfPath,
            new DateTime(2026, 11, 5),
            records,
            records);

        using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Equal(1, pdf.PageCount);
        Assert.InRange(pdf.Pages[0].Width.Point, 595.2, 595.4);
        Assert.InRange(pdf.Pages[0].Height.Point, 841.8, 842.0);
    }

    [Fact]
    public void UsbBackupCopiesMissingFilesAndVerifiesEverySourceFile()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "MaintenanceRecordTests",
            $"usb-backup-{Guid.NewGuid():N}");
        var source = Path.Combine(testRoot, "管网维修数据");
        var usbRoot = Path.Combine(testRoot, "USB");
        var similarTarget = Path.Combine(usbRoot, "管网维修数据备份");
        Directory.CreateDirectory(Path.Combine(source, "图片", "工单一号", "维修前"));
        Directory.CreateDirectory(similarTarget);
        File.WriteAllText(Path.Combine(source, "maintenance.db"), "database-source");
        File.WriteAllText(
            Path.Combine(source, "图片", "工单一号", "维修前", "现场照片.jpg"),
            "image-source");
        File.WriteAllText(Path.Combine(similarTarget, "maintenance.db"), "database-source");

        var drive = new UsbDriveInfo(usbRoot, "测试U盘", long.MaxValue);
        var result = UsbBackupService.Backup(source, drive);

        Assert.Equal(similarTarget, result.DestinationDirectory);
        Assert.Equal(2, result.VerifiedFiles);
        Assert.True(
            File.Exists(Path.Combine(similarTarget, "图片", "工单一号", "维修前", "现场照片.jpg")));
        Assert.Equal(
            0,
            File.ReadAllText(Path.Combine(source, "maintenance.db")).CompareTo(
                File.ReadAllText(Path.Combine(similarTarget, "maintenance.db"))));

        File.WriteAllText(Path.Combine(source, "新增文件.txt"), "new-file");
        var secondResult = UsbBackupService.Backup(source, drive);
        Assert.Equal(similarTarget, secondResult.DestinationDirectory);
        Assert.Equal(1, secondResult.CopiedFiles);
        Assert.Equal(2, secondResult.SkippedFiles);
        Assert.Equal(3, secondResult.VerifiedFiles);

        var restoredLocal = Path.Combine(testRoot, "restored-local");
        Directory.CreateDirectory(restoredLocal);
        var importResult = UsbBackupService.Import(restoredLocal, drive);
        Assert.Equal(restoredLocal, importResult.DestinationDirectory);
        Assert.Equal(3, importResult.VerifiedFiles);
        Assert.Equal(
            "new-file",
            File.ReadAllText(Path.Combine(restoredLocal, "新增文件.txt")));
    }

    [Fact]
    public void PdfKeepsLongMaterialListsOnSingleA4Page()
    {
        Directory.CreateDirectory(ArtifactDirectory);
        var record = CreateCompleteRecord("JLYL-2026-GWB-1104-01");
        for (var index = 2; index <= 10; index++)
        {
            record.Materials.Add(new RecordMaterial
            {
                Name = $"测试材料 {index}",
                Specification = $"DN{index * 10}",
                Unit = "个",
                Quantity = index,
                UnitPrice = 10m + index,
                Note = "仓库领用",
            });
        }

        var pdfPath = Path.Combine(ArtifactDirectory, "维修记录-多材料.pdf");
        PdfReportService.Generate(pdfPath, record);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
    }

    private static MaintenanceRecord CreateCompleteRecord(string orderNumber)
    {
        var record = new MaintenanceRecord
        {
            OrderNumber = orderNumber,
            Source = "客服转单",
            RepairPerson = "李华",
            Location = "中央半岛 5 栋 2 楼",
            RepairReason = "水压偏低，需要检查并更换减压阀。",
            Contact = "13900000000 于女士",
            RepairMethod = "更换阀门",
            WorkDate = new DateTime(2026, 11, 4),
            WorkClock = "09:08",
            CompletionDate = new DateTime(2026, 11, 4),
            CompletionClock = "11:30",
            HasOddJob = true,
            OddJobTime = "2小时",
            OddJobType = "管道工",
            OddJobPeople = 2,
            OddJobRateText = "45",
            LaborCompany = "文良辉",
            OddJobCostText = "180",
            HasMachinery = true,
            MachineryTime = "1小时",
            MachineryType = "60型小挖",
            MachineryRateText = "180",
            MachineryCostText = "120",
            Restored = true,
            RestorationType = "素土（原土）回填还原",
            RestorationUnitPriceText = "37",
            RestorationAreaText = "2.16",
            RestorationCostText = "80",
            Accepted = true,
            PreparedBy = "李慧",
            ReviewDepartment = "管网部",
            ResponsiblePerson = "王工",
            Notes = "维修后压力恢复正常。",
            Status = MaintenanceRecordStatus.Completed,
            StatusInitialized = true,
        };
        record.Materials.Add(new RecordMaterial
        {
            Name = "铜加密阀",
            Specification = "DN20",
            Unit = "个",
            Quantity = 1,
            UnitPrice = 120.5m,
            Note = "仓库领用",
        });
        record.HookCollections();
        record.NotifyAll();
        return record;
    }
}
