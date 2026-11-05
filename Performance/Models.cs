namespace Performance;

public sealed record SystemSnapshot(
    double CpuUsage,
    double? CpuTemperature,
    double RamUsedGb,
    double RamTotalGb,
    double RamPercent,
    double? GpuUsage,
    double? GpuTemperature,
    double? VramUsedGb,
    double? VramTotalGb,
    string GpuName,
    double DriveUsedGb,
    double DriveTotalGb,
    double DrivePercent,
    DateTime SampledAt);

public sealed record ProcessRow(
    int Pid,
    string Name,
    double MemoryMb,
    double CpuPercent,
    string Path,
    string Session,
    bool IsSystem);

public sealed record TcpConnectionRow(
    int Pid,
    string ProcessName,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State);

public sealed record InstalledAppRow(
    string DisplayName,
    string Publisher,
    string Version,
    string InstallLocation,
    string UninstallString,
    string QuietUninstallString,
    double EstimatedSizeMb);

public sealed record ResidualFolderRow(string Path, double SizeMb);

public sealed record StorageFileRow(
    string Name,
    string FullPath,
    string Category,
    double SizeMb,
    DateTime Modified);

public sealed record StorageScanResult(
    long TotalBytes,
    int FilesScanned,
    IReadOnlyList<StorageFileRow> LargestFiles,
    IReadOnlyDictionary<string, double> CategoryMb);
