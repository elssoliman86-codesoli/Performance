using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Performance.Services;

public sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer;
    private ulong _lastIdle, _lastKernel, _lastUser;
    private bool _hasCpuSample;

    public HardwareMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = false,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsNetworkEnabled = false
        };
        try { _computer.Open(); } catch { }
    }

    public SystemSnapshot Read()
    {
        double cpu = ReadCpuUsage();
        var memory = ReadMemory();
        double? cpuTemp = null;
        double? gpuTemp = null;
        double? gpuLoad = null;
        string gpuName = "GPU sensor unavailable";
        try
        {
            foreach (var hardware in _computer.Hardware)
                ReadHardware(hardware, ref cpuTemp, ref gpuTemp, ref gpuLoad, ref gpuName);
        }
        catch { }

        var nvidia = ReadNvidiaSmi();
        if (nvidia is not null)
        {
            gpuName = nvidia.Value.Name;
            gpuTemp = nvidia.Value.Temp ?? gpuTemp;
            gpuLoad = nvidia.Value.Load ?? gpuLoad;
        }

        double? vramUsed = nvidia?.VramUsed;
        double? vramTotal = nvidia?.VramTotal;
        var drive = ReadSystemDrive();

        return new SystemSnapshot(cpu, cpuTemp, memory.UsedGb, memory.TotalGb,
            memory.TotalGb <= 0 ? 0 : memory.UsedGb / memory.TotalGb * 100,
            gpuLoad, gpuTemp, vramUsed, vramTotal, gpuName,
            drive.UsedGb, drive.TotalGb,
            drive.TotalGb <= 0 ? 0 : drive.UsedGb / drive.TotalGb * 100,
            DateTime.Now);
    }

    private static void ReadHardware(IHardware hardware, ref double? cpuTemp, ref double? gpuTemp, ref double? gpuLoad, ref string gpuName)
    {
        try { hardware.Update(); } catch { }
        if (hardware.HardwareType == HardwareType.Cpu)
        {
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                {
                    var name = sensor.Name.ToLowerInvariant();
                    if (name.Contains("package") || name.Contains("tctl") || name.Contains("die"))
                        cpuTemp = Math.Max(cpuTemp ?? 0, sensor.Value.Value);
                    else if (cpuTemp is null && name.Contains("core"))
                        cpuTemp = sensor.Value.Value;
                }
            }
        }
        if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
        {
            gpuName = hardware.Name;
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.Value is null) continue;
                if (sensor.SensorType == SensorType.Temperature) gpuTemp = sensor.Value.Value;
                if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    gpuLoad = sensor.Value.Value;
            }
        }
        foreach (var child in hardware.SubHardware)
            ReadHardware(child, ref cpuTemp, ref gpuTemp, ref gpuLoad, ref gpuName);
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong i = ToUInt64(idle), k = ToUInt64(kernel), u = ToUInt64(user);
        if (!_hasCpuSample)
        {
            _lastIdle = i; _lastKernel = k; _lastUser = u; _hasCpuSample = true;
            return 0;
        }
        ulong idleDelta = i - _lastIdle;
        ulong totalDelta = (k - _lastKernel) + (u - _lastUser);
        _lastIdle = i; _lastKernel = k; _lastUser = u;
        if (totalDelta == 0) return 0;
        return Math.Clamp((double)(totalDelta - Math.Min(totalDelta, idleDelta)) / totalDelta * 100, 0, 100);
    }

    private static ulong ToUInt64(FILETIME value) => ((ulong)value.dwHighDateTime << 32) | value.dwLowDateTime;

    private static (double UsedGb, double TotalGb) ReadMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
        double total = status.ullTotalPhys / 1024d / 1024d / 1024d;
        double free = status.ullAvailPhys / 1024d / 1024d / 1024d;
        return (Math.Max(0, total - free), total);
    }

    private static (double UsedGb, double TotalGb) ReadSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var d = new DriveInfo(root);
            double total = d.TotalSize / 1024d / 1024d / 1024d;
            return ((d.TotalSize - d.AvailableFreeSpace) / 1024d / 1024d / 1024d, total);
        }
        catch { return (0, 0); }
    }

    private static (string Name, double? Temp, double? Load, double? VramUsed, double? VramTotal)? ReadNvidiaSmi()
    {
        string? exe = FindNvidiaSmi();
        if (exe is null) return null;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                }
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(1800)) { try { process.Kill(); } catch { } return null; }
            string line = outputTask.GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var p = line.Split(',').Select(x => x.Trim()).ToArray();
            if (p.Length < 5) return null;
            return (p[0], Number(p[1]), Number(p[2]), Number(p[3]) / 1024d, Number(p[4]) / 1024d);
        }
        catch { return null; }
    }

    private static double? Number(string value) => double.TryParse(value, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string? FindNvidiaSmi()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe")
        };
        foreach (var item in candidates) if (File.Exists(item)) return item;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(part.Trim(), "nvidia-smi.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public void Dispose()
    {
        try { _computer.Close(); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint dwLowDateTime; public uint dwHighDateTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
