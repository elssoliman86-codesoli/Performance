using System.Diagnostics;
using System.Text.Json;

namespace Performance.Services;

public sealed class NetworkMonitor
{
    public async Task<IReadOnlyList<TcpConnectionRow>> GetTcpConnectionsAsync(CancellationToken cancellationToken = default)
    {
        const string command = "Get-NetTCPConnection -ErrorAction SilentlyContinue | Select-Object OwningProcess,LocalAddress,LocalPort,RemoteAddress,RemotePort,State | ConvertTo-Json -Compress";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command + "\"",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                }
            };
            process.Start();
            string json = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<TcpConnectionRow>();
            using var doc = JsonDocument.Parse(json);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToArray()
                : new[] { doc.RootElement };
            var result = new List<TcpConnectionRow>();
            foreach (var item in elements)
            {
                int pid = GetInt(item, "OwningProcess");
                string name = "Unknown";
                try { using var p = Process.GetProcessById(pid); name = p.ProcessName; } catch { }
                result.Add(new TcpConnectionRow(pid, name, GetString(item, "LocalAddress"), GetInt(item, "LocalPort"),
                    GetString(item, "RemoteAddress"), GetInt(item, "RemotePort"), GetString(item, "State")));
            }
            return result.OrderBy(x => x.ProcessName).ToList();
        }
        catch { return Array.Empty<TcpConnectionRow>(); }
    }

    private static int GetInt(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static string GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? value.ToString() : "";
}
