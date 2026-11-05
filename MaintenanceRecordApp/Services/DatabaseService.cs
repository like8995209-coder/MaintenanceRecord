using System.Globalization;
using System.IO;
using System.Text.Json;
using MaintenanceRecordApp.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace MaintenanceRecordApp.Services;

public sealed class DatabaseService
{
    public const string RepairMethodCategory = "repair_method";
    public const string RepairPersonCategory = "repair_person";
    public const string LaborCompanyCategory = "labor_company";
    public const string OddJobTypeCategory = "odd_job_type";
    public const string MachineryTypeCategory = "machinery_type";
    public const string RestorationTypeCategory = "restoration_type";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _connectionString;

    public DatabaseService(string? dataDirectory = null)
    {
        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MaintenanceRecord",
            "Data");
        if (dataDirectory is null)
        {
            dataDirectory = ResolveDataDirectory(legacyDirectory);
            MigrateLegacyDatabase(legacyDirectory, dataDirectory);
        }
        Directory.CreateDirectory(dataDirectory);
        DataDirectory = dataDirectory;
        DatabasePath = Path.Combine(dataDirectory, "maintenance.db");
        TodayReportsDirectory = Path.Combine(dataDirectory, "今日");
        RecordReportsDirectory = Path.Combine(dataDirectory, "维修记录");
        LibraryDirectory = Path.Combine(dataDirectory, "库");
        ImagesDirectory = Path.Combine(dataDirectory, "图片");
        foreach (var directory in new[]
                 {
                     TodayReportsDirectory,
                     RecordReportsDirectory,
                     LibraryDirectory,
                     ImagesDirectory,
                 })
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        Initialize();
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public string TodayReportsDirectory { get; }

    public string RecordReportsDirectory { get; }

    public string LibraryDirectory { get; }

    public string ImagesDirectory { get; }

    public void Checkpoint()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    public string PrepareForImport()
    {
        Checkpoint();
        Directory.CreateDirectory(LibraryDirectory);
        var backupPath = Path.Combine(
            LibraryDirectory,
            $"本地数据库_导入前_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db");
        using (var source = OpenConnection())
        using (var destination = new SqliteConnection(
                   new SqliteConnectionStringBuilder
                   {
                       DataSource = backupPath,
                       Mode = SqliteOpenMode.ReadWriteCreate,
                       Pooling = false,
                   }.ToString()))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = DatabasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        return backupPath;
    }

    public void CompleteImport()
    {
        SqliteConnection.ClearAllPools();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"导入后的数据库完整性检查失败：{result}");
        }
    }

    public void RestoreDatabaseBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("找不到导入前的数据库安全副本。", backupPath);
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var target = DatabasePath + suffix;
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
        File.Copy(backupPath, DatabasePath);
        SqliteConnection.ClearAllPools();
    }

    private static string ResolveDataDirectory(string legacyDirectory)
    {
        var configuredPath = ReadConfiguredDataDirectory();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (CanUseConfiguredDirectory(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            var fallback = DesktopDataDirectory();
            WriteConfiguredDataDirectory(fallback);
            return fallback;
        }

        return legacyDirectory;
    }

    private static string ReadConfiguredDataDirectory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\M4099\MaintenanceRecord");
            return (key?.GetValue("DataPath") as string)?.Trim().Trim('"') ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool CanUseConfiguredDirectory(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
        }
        catch
        {
            return false;
        }
    }

    private static string DesktopDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "管网维修数据");

    private static void WriteConfiguredDataDirectory(string path)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\M4099\MaintenanceRecord");
            key?.SetValue("DataPath", path, RegistryValueKind.String);
        }
        catch
        {
            // The selected directory remains usable even if the sidecar file is read-only.
        }
    }

    private static void MigrateLegacyDatabase(string sourceDirectory, string targetDirectory)
    {
        try
        {
            if (string.Equals(
                    Path.GetFullPath(sourceDirectory),
                    Path.GetFullPath(targetDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Directory.CreateDirectory(targetDirectory);
            var targetDatabase = Path.Combine(targetDirectory, "maintenance.db");
            if (File.Exists(targetDatabase))
            {
                return;
            }

            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var source = Path.Combine(sourceDirectory, "maintenance.db" + suffix);
                if (File.Exists(source))
                {
                    File.Copy(source, Path.Combine(targetDirectory, "maintenance.db" + suffix));
                }
            }
        }
        catch
        {
            // A new database will be created if migration is unavailable.
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS Records (
                Id TEXT PRIMARY KEY,
                OrderNumber TEXT NOT NULL UNIQUE,
                WorkDate TEXT NOT NULL,
                Location TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                Payload TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Materials (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Specification TEXT NOT NULL DEFAULT '',
                Unit TEXT NOT NULL DEFAULT '',
                Price TEXT NULL,
                UNIQUE(Name, Specification, Unit)
            );

            CREATE TABLE IF NOT EXISTS Options (
                Category TEXT NOT NULL,
                Value TEXT NOT NULL,
                PRIMARY KEY(Category, Value)
            );

            CREATE TABLE IF NOT EXISTS OrderNumberReservations (
                OrderNumber TEXT PRIMARY KEY,
                ReservedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        using var seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT OR IGNORE INTO Options(Category, Value) VALUES
                ('repair_method', '更换阀门'),
                ('repair_method', '更换管件'),
                ('repair_method', '开挖维修'),
                ('repair_method', '抢修'),
                ('repair_method', '排查处理'),
                ('repair_method', '其他'),
                ('repair_person', '管网部员工'),
                ('odd_job_type', '挖工'),
                ('odd_job_type', '辅助管道工'),
                ('odd_job_type', '管道工'),
                ('machinery_type', '60型小挖'),
                ('machinery_type', '120/150型中挖'),
                ('machinery_type', '200型大挖'),
                ('restoration_type', '素土（原土）回填还原'),
                ('restoration_type', '砂石（级配碎石）路面还原'),
                ('restoration_type', '水泥混凝土路面还原'),
                ('restoration_type', '沥青（黑化）路面还原'),
                ('restoration_type', '12cm改性沥青面层＋原水泥砼路面'),
                ('restoration_type', '水泥方砖人行道'),
                ('restoration_type', '彩色方砖人行道'),
                ('restoration_type', '现浇人行道'),
                ('restoration_type', '条（料）石路面'),
                ('restoration_type', '站石、卧石（路缘石）'),
                ('restoration_type', '外运多余土方（或运回填土）');
            """;
        seed.ExecuteNonQuery();
    }

    public string CreateNextOrderNumber(DateTime date)
    {
        var prefix = $"JLYL-{date:yyyy}-GWB-{date:MMdd}-";
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT OrderNumber FROM Records WHERE OrderNumber LIKE $prefix
            UNION
            SELECT OrderNumber FROM OrderNumberReservations WHERE OrderNumber LIKE $prefix;
            """;
        command.Parameters.AddWithValue("$prefix", prefix + "%");
        using var reader = command.ExecuteReader();
        var highest = 0;
        while (reader.Read())
        {
            var number = reader.GetString(0);
            var sequenceText = number[prefix.Length..];
            if (int.TryParse(sequenceText, out var sequence))
            {
                highest = Math.Max(highest, sequence);
            }
        }
        reader.Close();

        var orderNumber = prefix + (highest + 1).ToString("00", CultureInfo.InvariantCulture);
        using var reserve = connection.CreateCommand();
        reserve.Transaction = transaction;
        reserve.CommandText =
            "INSERT INTO OrderNumberReservations(OrderNumber, ReservedAt) VALUES ($number, $reservedAt);";
        reserve.Parameters.AddWithValue("$number", orderNumber);
        reserve.Parameters.AddWithValue("$reservedAt", DateTime.UtcNow.ToString("O"));
        reserve.ExecuteNonQuery();
        transaction.Commit();
        return orderNumber;
    }

    public IReadOnlyList<string> LoadOptions(string category)
    {
        var values = new List<string>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Value FROM Options WHERE Category = $category ORDER BY Value COLLATE NOCASE;";
        command.Parameters.AddWithValue("$category", category);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }
        return values;
    }

    public void AddOption(string category, string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR IGNORE INTO Options(Category, Value) VALUES ($category, $value);";
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public void RenameOption(string category, string oldValue, string newValue)
    {
        newValue = newValue.Trim();
        if (string.IsNullOrWhiteSpace(newValue) || oldValue == newValue)
        {
            return;
        }
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText =
            "DELETE FROM Options WHERE Category = $category AND Value = $oldValue;";
        delete.Parameters.AddWithValue("$category", category);
        delete.Parameters.AddWithValue("$oldValue", oldValue);
        delete.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            "INSERT OR IGNORE INTO Options(Category, Value) VALUES ($category, $newValue);";
        insert.Parameters.AddWithValue("$category", category);
        insert.Parameters.AddWithValue("$newValue", newValue);
        insert.ExecuteNonQuery();
        transaction.Commit();
    }

    public void DeleteOption(string category, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM Options WHERE Category = $category AND Value = $value;";
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<MaterialCatalogItem> LoadMaterials()
    {
        var values = new List<MaterialCatalogItem>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Name, Specification, Unit, Price FROM Materials ORDER BY Name COLLATE NOCASE, Specification COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(new MaterialCatalogItem
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Specification = reader.GetString(2),
                Unit = reader.GetString(3),
                Price = reader.IsDBNull(4)
                    ? null
                    : decimal.TryParse(
                        reader.GetString(4),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var price)
                        ? price
                        : null,
            });
        }
        return values;
    }

    public int ImportMaterials(IEnumerable<MaterialCatalogItem> materials)
    {
        var count = 0;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var material in materials)
        {
            if (string.IsNullOrWhiteSpace(material.Name))
            {
                continue;
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Materials(Name, Specification, Unit, Price)
                VALUES ($name, $specification, $unit, $price)
                ON CONFLICT(Name, Specification, Unit)
                DO UPDATE SET Price = COALESCE(excluded.Price, Materials.Price);
                """;
            command.Parameters.AddWithValue("$name", material.Name.Trim());
            command.Parameters.AddWithValue("$specification", material.Specification.Trim());
            command.Parameters.AddWithValue("$unit", material.Unit.Trim());
            command.Parameters.AddWithValue(
                "$price",
                material.Price.HasValue
                    ? material.Price.Value.ToString(CultureInfo.InvariantCulture)
                    : DBNull.Value);
            count += command.ExecuteNonQuery();
        }
        transaction.Commit();
        return count;
    }

    public void SaveMaterial(MaterialCatalogItem material)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = material.Id == 0
            ? """
              INSERT INTO Materials(Name, Specification, Unit, Price)
              VALUES ($name, $specification, $unit, $price);
              SELECT last_insert_rowid();
              """
            : """
              UPDATE Materials SET
                  Name = $name,
                  Specification = $specification,
                  Unit = $unit,
                  Price = $price
              WHERE Id = $id;
              """;
        command.Parameters.AddWithValue("$name", material.Name.Trim());
        command.Parameters.AddWithValue("$specification", material.Specification.Trim());
        command.Parameters.AddWithValue("$unit", material.Unit.Trim());
        command.Parameters.AddWithValue(
            "$price",
            material.Price.HasValue
                ? material.Price.Value.ToString(CultureInfo.InvariantCulture)
                : DBNull.Value);
        if (material.Id == 0)
        {
            material.Id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        else
        {
            command.Parameters.AddWithValue("$id", material.Id);
            command.ExecuteNonQuery();
        }
    }

    public void DeleteMaterial(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Materials WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SaveRecord(MaintenanceRecord record)
    {
        var payload = JsonSerializer.Serialize(record, JsonOptions);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Records(Id, OrderNumber, WorkDate, Location, UpdatedAt, Payload)
            VALUES ($id, $orderNumber, $workDate, $location, $updatedAt, $payload)
            ON CONFLICT(Id) DO UPDATE SET
                OrderNumber = excluded.OrderNumber,
                WorkDate = excluded.WorkDate,
                Location = excluded.Location,
                UpdatedAt = excluded.UpdatedAt,
                Payload = excluded.Payload;
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$orderNumber", record.OrderNumber);
        command.Parameters.AddWithValue("$workDate", record.WorkDate?.ToString("yyyy-MM-dd") ?? "");
        command.Parameters.AddWithValue("$location", record.Location);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();

        using var release = connection.CreateCommand();
        release.CommandText =
            "DELETE FROM OrderNumberReservations WHERE OrderNumber = $orderNumber;";
        release.Parameters.AddWithValue("$orderNumber", record.OrderNumber);
        release.ExecuteNonQuery();
    }

    public IReadOnlyList<MaintenanceRecord> LoadRecords()
    {
        var records = new List<MaintenanceRecord>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Payload FROM Records ORDER BY WorkDate DESC, OrderNumber DESC;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var record = JsonSerializer.Deserialize<MaintenanceRecord>(reader.GetString(0), JsonOptions);
                if (record is not null)
                {
                    record.HookCollections();
                    record.NormalizeStatus();
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
                // Keep other records available if one legacy payload is malformed.
            }
        }
        return records;
    }

    public void DeleteRecord(string recordId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Records WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", recordId);
        command.ExecuteNonQuery();
    }
}
