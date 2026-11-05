using System.Diagnostics;

namespace Performance.Services;

public sealed class ProcessMonitor
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _previous = new();

    public IReadOnlyList<ProcessRow> GetProcesses()
    {
        var now = DateTime.UtcNow;
        var rows = new List<ProcessRow>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                p.Refresh();
                int pid = p.Id;
                string name = p.ProcessName;
                double memory = p.WorkingSet64 / 1024d / 1024d;
                string path = "";
                try { path = p.MainModule?.FileName ?? ""; } catch { }
                string session = "Background";
                try { session = p.SessionId == 0 ? "System" : "User"; } catch { }
                double cpu = 0;
                try
                {
                    var current = p.TotalProcessorTime;
                    if (_previous.TryGetValue(pid, out var old))
                    {
                        var seconds = (now - old.At).TotalSeconds;
                        if (seconds > 0)
                            cpu = Math.Clamp((current - old.Cpu).TotalMilliseconds / (seconds * 1000d * Environment.ProcessorCount) * 100, 0, 100);
                    }
                    _previous[pid] = (current, now);
                }
                catch { }
                rows.Add(new ProcessRow(pid, name, memory, cpu, path, session, session == "System"));
            }
            catch { }
            finally { p.Dispose(); }
        }
        var active = rows.Select(x => x.Pid).ToHashSet();
        foreach (var id in _previous.Keys.Where(x => !active.Contains(x)).ToList()) _previous.Remove(id);
        return rows.OrderByDescending(x => x.MemoryMb).ToList();
    }

    public static bool StopProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch { return false; }
    }
}
