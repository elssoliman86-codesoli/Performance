using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Performance.Services;

public sealed class PresentMonService : IDisposable
{
    private Process? _process;
    private readonly Queue<double> _frameTimes = new();
    private readonly object _gate = new();
    private string? _csvHeader;
    private int _frameColumn = -1;
    public double? Fps { get; private set; }
    public string Status { get; private set; } = "Not started";
    public string? LastError { get; private set; }

    public bool Start(int pid)
    {
        Stop();
        string? exe = EnsurePresentMon();
        if (exe is null) { Status = "PresentMon.exe missing"; LastError = "PresentMon.exe could not be found or extracted."; return false; }
        Status = "Starting capture…"; LastError = null;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--process_id {pid} --output_stdout --no_console_stats --terminate_on_proc_exit",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data is not null) ConsumeLine(e.Data); };
            _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LastError = e.Data; };\n            _process.Exited += (_, _) => { Status = $"PresentMon exited (code {_process?.ExitCode})"; };
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; Status = "Capture failed"; Stop(); return false; }
    }

    private void ConsumeLine(string line)
    {
        lock (_gate)
        {
            if (_csvHeader is null)
            {
                _csvHeader = line;
                var headers = ParseCsv(line);
                _frameColumn = headers.FindIndex(x => x.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
                if (_frameColumn < 0) _frameColumn = headers.FindIndex(x => x.Equals("MsBetweenDisplayChange", StringComparison.OrdinalIgnoreCase));
                return;
            }
            if (_frameColumn < 0) return;
            var cells = ParseCsv(line);
            if (_frameColumn >= cells.Count || !double.TryParse(cells[_frameColumn], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var ms) || ms <= 0 || ms > 1000) return;
            _frameTimes.Enqueue(ms);
            while (_frameTimes.Count > 120) _frameTimes.Dequeue();
            if (_frameTimes.Count >= 10)
                Fps = 1000d / _frameTimes.Average();
        }
    }

    private static List<string> ParseCsv(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }

    private static string? EnsurePresentMon()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
        if (File.Exists(beside)) return beside;
        try
        {
            var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Performance.Assets.PresentMon.exe");
            if (resource is null) return null;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Performance", "tools");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "PresentMon.exe");
            using (resource)
            using (var output = File.Create(path)) resource.CopyTo(output);
            return path;
        }
        catch { return null; }
    }

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
            _process?.Dispose();
        }
        catch { }
        _process = null;
        lock (_gate) { _csvHeader = null; _frameColumn = -1; _frameTimes.Clear(); Fps = null; }
    }

    public void Dispose() => Stop();
}
