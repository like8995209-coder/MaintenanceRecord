using System.IO;
using System.Security.Cryptography;

namespace MaintenanceRecordApp.Services;

public sealed record UsbDriveInfo(string RootPath, string DisplayName, long FreeSpace);

public sealed record UsbBackupProgress(int TotalFiles, int CompletedFiles, string CurrentFile);

public sealed record UsbBackupResult(
    string DestinationDirectory,
    int CopiedFiles,
    int SkippedFiles,
    int VerifiedFiles,
    long VerifiedBytes);

public static class UsbBackupService
{
    public static IReadOnlyList<UsbDriveInfo> DetectUsbDrives()
    {
        var drives = new List<UsbDriveInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady ||
                    drive.DriveType != DriveType.Removable ||
                    drive.TotalSize <= 0)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? "可移动存储设备"
                    : drive.VolumeLabel;
                drives.Add(
                    new UsbDriveInfo(
                        drive.RootDirectory.FullName,
                        $"{label}（{drive.Name.TrimEnd('\\')}，可用 {FormatSize(drive.AvailableFreeSpace)}）",
                        drive.AvailableFreeSpace));
            }
            catch
            {
                // An ejected or inaccessible drive is not a backup candidate.
            }
        }
        return drives;
    }

    public static UsbBackupResult Backup(
        string sourceDirectory,
        UsbDriveInfo drive,
        IProgress<UsbBackupProgress>? progress = null)
    {
        EnsureUsbAvailable(drive);
        var sourceRoot = NormalizeDirectory(sourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"本机数据文件夹不存在：{sourceRoot}");
        }

        var targetRoot = FindOrCreateBackupRoot(drive.RootPath);
        return Synchronize(
            sourceRoot,
            targetRoot,
            drive.FreeSpace,
            progress);
    }

    public static UsbBackupResult Import(
        string localDirectory,
        UsbDriveInfo drive,
        IProgress<UsbBackupProgress>? progress = null)
    {
        EnsureUsbAvailable(drive);
        var targetRoot = NormalizeDirectory(localDirectory);
        Directory.CreateDirectory(targetRoot);
        var sourceRoot = FindExistingBackupRoot(drive.RootPath)
            ?? throw new DirectoryNotFoundException(
                "U 盘中未找到“管网维修数据”或名称相近的备份文件夹，请先完成 U 盘备份。");
        var availableSpace = new DriveInfo(Path.GetPathRoot(targetRoot)!).AvailableFreeSpace;
        return Synchronize(sourceRoot, targetRoot, availableSpace, progress);
    }

    private static UsbBackupResult Synchronize(
        string sourceRoot,
        string targetRoot,
        long availableSpace,
        IProgress<UsbBackupProgress>? progress)
    {
        sourceRoot = NormalizeDirectory(sourceRoot);
        targetRoot = NormalizeDirectory(targetRoot);
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase) ||
            targetRoot.StartsWith(
                sourceRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            sourceRoot.StartsWith(
                targetRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("源文件夹和目标文件夹不能互相包含。");
        }

        var directories = Directory
            .EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories)
            .ToList();
        var files = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .ToList();
        var bytesStillNeeded = ComputeMissingBytes(files, sourceRoot, targetRoot);
        if (availableSpace < bytesStillNeeded)
        {
            throw new IOException(
                $"目标磁盘可用空间不足。仍需约 {FormatSize(bytesStillNeeded)}，当前可用 {FormatSize(availableSpace)}。");
        }

        Directory.CreateDirectory(targetRoot);
        foreach (var directory in directories)
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        var totalFiles = files.Count;
        var completed = 0;
        var copied = 0;
        var skipped = 0;
        foreach (var sourceFile in files)
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationFile = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

            if (NeedsCopy(sourceFile, destinationFile))
            {
                CopyFile(sourceFile, destinationFile);
                copied++;
            }
            else
            {
                skipped++;
            }

            completed++;
            progress?.Report(new UsbBackupProgress(totalFiles, completed, relative));
        }

        var verifiedFiles = 0;
        long verifiedBytes = 0;
        foreach (var sourceFile in files)
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationFile = Path.Combine(targetRoot, relative);
            if (!File.Exists(destinationFile) || !FilesMatch(sourceFile, destinationFile))
            {
                CopyFile(sourceFile, destinationFile);
                if (!FilesMatch(sourceFile, destinationFile))
                {
                    throw new IOException($"文件校验失败：{relative}");
                }
            }

            verifiedFiles++;
            verifiedBytes += new FileInfo(sourceFile).Length;
        }

        foreach (var directory in directories)
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            if (!Directory.Exists(Path.Combine(targetRoot, relative)))
            {
                throw new IOException($"文件夹校验失败：{relative}");
            }
        }

        return new UsbBackupResult(
            targetRoot,
            copied,
            skipped,
            verifiedFiles,
            verifiedBytes);
    }

    private static void EnsureUsbAvailable(UsbDriveInfo drive)
    {
        if (!Directory.Exists(Path.GetFullPath(drive.RootPath)))
        {
            throw new IOException("U 盘已经断开连接，请重新插入后再试。");
        }
    }

    private static string FindOrCreateBackupRoot(string driveRoot)
    {
        var existing = FindExistingBackupRoot(driveRoot);
        if (existing is not null)
        {
            return existing;
        }

        var target = Path.Combine(Path.GetFullPath(driveRoot), "管网维修数据");
        Directory.CreateDirectory(target);
        return target;
    }

    private static string? FindExistingBackupRoot(string driveRoot)
    {
        return Directory.EnumerateDirectories(Path.GetFullPath(driveRoot))
            .Select(path => new DirectoryInfo(path))
            .Where(directory => IsSimilarBackupFolder(directory.Name))
            .OrderByDescending(directory =>
                string.Equals(directory.Name, "管网维修数据", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(directory => directory.CreationTimeUtc)
            .Select(directory => directory.FullName)
            .FirstOrDefault();
    }

    private static bool IsSimilarBackupFolder(string name)
    {
        var normalized = NormalizeFolderName(name);
        return normalized == "管网维修数据" ||
               normalized.StartsWith("管网维修数据", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("管网维修数据", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFolderName(string name) =>
        new(
            name.Where(character => char.IsLetterOrDigit(character))
                .Select(char.ToLowerInvariant)
                .ToArray());

    private static bool NeedsCopy(string sourceFile, string destinationFile)
    {
        if (!File.Exists(destinationFile))
        {
            return true;
        }

        var source = new FileInfo(sourceFile);
        var destination = new FileInfo(destinationFile);
        return source.Length != destination.Length ||
               source.LastWriteTimeUtc != destination.LastWriteTimeUtc;
    }

    private static long ComputeMissingBytes(
        IReadOnlyList<string> files,
        string sourceRoot,
        string targetRoot)
    {
        long bytesStillNeeded = 0;
        foreach (var sourceFile in files)
        {
            var destination = Path.Combine(
                targetRoot,
                Path.GetRelativePath(sourceRoot, sourceFile));
            if (NeedsCopy(sourceFile, destination))
            {
                bytesStillNeeded += new FileInfo(sourceFile).Length;
            }
        }
        return bytesStillNeeded;
    }

    private static bool FilesMatch(string sourceFile, string destinationFile)
    {
        var source = new FileInfo(sourceFile);
        var destination = new FileInfo(destinationFile);
        return source.Length == destination.Length &&
               string.Equals(
                   ComputeSha256(sourceFile),
                   ComputeSha256(destinationFile),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static void CopyFile(string sourceFile, string destinationFile)
    {
        using var source = new FileStream(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var destination = new FileStream(
            destinationFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
        File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
