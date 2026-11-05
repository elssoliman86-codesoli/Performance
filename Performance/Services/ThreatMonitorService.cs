using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Performance.Services;

public sealed record ThreatFinding(
    DateTime FirstSeenUtc, DateTime LastSeenUtc, int Pid, string ProcessName, string ExecutablePath,
    double MemoryMb, double CpuPercent, string Sha256, string Publisher, string SignatureStatus,
    string Risk, int RiskScore, string Reasons, string NetworkEndpoints);

/// <summary>
/// Read-only process inventory and explainable heuristics. It never terminates or quarantines anything.
/// </summary>
public sealed class ThreatMonitorService
{
    private readonly Dictionary<int, (DateTime First, string Path, double Memory, DateTime Last)> _seen = new();
    private readonly Dictionary<int, double> _previousMemory = new();
    private readonly string _reportRoot;
    private readonly Dictionary<string, (DateTime LastWriteUtc, long Length, string Hash, string Publisher, string Signature)> _fileEvidence = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastReportUtc = DateTime.MinValue;

    public ThreatMonitorService(string reportRoot)
    {
        _reportRoot = Path.GetFullPath(reportRoot);
        Directory.CreateDirectory(_reportRoot);
    }

    public async Task<IReadOnlyList<ThreatFinding>> CaptureAsync(
        IReadOnlyList<ProcessRow> processes, IReadOnlyList<TcpConnectionRow> connections,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var endpoints = connections.GroupBy(x => x.Pid).ToDictionary(g => g.Key,
            g => string.Join("; ", g.Select(x => $"{x.RemoteAddress}:{x.RemotePort} [{x.State}]").Distinct().Take(20)));
        var findings = new List<ThreatFinding>();
        foreach (var p in processes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(p.Path) || !File.Exists(p.Path)) continue;
            if (!_seen.TryGetValue(p.Pid, out var state) || !string.Equals(state.Path, p.Path, StringComparison.OrdinalIgnoreCase))
                state = (now, p.Path, p.MemoryMb, now);
            double prior = _previousMemory.GetValueOrDefault(p.Pid, p.MemoryMb);
            double growth = p.MemoryMb - prior;
            _previousMemory[p.Pid] = p.MemoryMb;
            _seen[p.Pid] = (state.First, p.Path, p.MemoryMb, now);

            string hash = "", publisher = "Unknown", signature = "Not verified";
            try
            {
                var info = new FileInfo(p.Path);
                if (_fileEvidence.TryGetValue(p.Path, out var cached) && cached.LastWriteUtc == info.LastWriteTimeUtc && cached.Length == info.Length)
                {
                    (hash, publisher, signature) = (cached.Hash, cached.Publisher, cached.Signature);
                }
                else
                {
                    using var stream = new FileStream(p.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    try
                    {
                        var cert = X509Certificate.CreateFromSignedFile(p.Path);
                        using var cert2 = new X509Certificate2(cert);
                        publisher = cert2.GetNameInfo(X509NameType.SimpleName, false);
                        signature = cert2.Verify() ? "Certificate chain valid" : "Certificate present; chain not trusted";
                    }
                    catch { signature = "Unsigned or signature unavailable"; }
                    _fileEvidence[p.Path] = (info.LastWriteTimeUtc, info.Length, hash, publisher, signature);
                }
            }
            catch { signature = "File evidence unavailable"; }

            int score = 0;
            var reasons = new List<string>();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            bool trustedLocation = IsUnder(p.Path, windows) || IsUnder(p.Path, programFiles) || IsUnder(p.Path, programFilesX86);
            if (!trustedLocation && (IsUnder(p.Path, Path.GetTempPath()) || IsUnder(p.Path, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))))
            { score += 20; reasons.Add("Executable runs from a user-writable or temporary location."); }
            if (signature.StartsWith("Unsigned", StringComparison.Ordinal))
            { score += 12; reasons.Add("No verifiable Authenticode signature was found."); }
            if (growth > Math.Max(350, prior * .8))
            { score += 15; reasons.Add($"Memory increased by {growth:0} MB since the previous sample."); }
            if (p.MemoryMb > 1800)
            { score += 8; reasons.Add("High working-set memory; this is a resource signal, not proof of malware."); }
            if (endpoints.ContainsKey(p.Pid) && !trustedLocation)
            { score += 5; reasons.Add("Process has active TCP endpoints and is outside standard install locations."); }
            if (string.IsNullOrWhiteSpace(publisher) || publisher == "Unknown")
            { score += 5; reasons.Add("Publisher identity is unavailable."); }
            string risk = score >= 40 ? "High" : score >= 25 ? "Medium" : score >= 12 ? "Low" : "Informational";
            if (reasons.Count == 0) reasons.Add("No configured heuristic was triggered. This is not a malware clearance.");
            findings.Add(new ThreatFinding(state.First, now, p.Pid, p.Name, p.Path, p.MemoryMb, p.CpuPercent,
                hash, publisher, signature, risk, score, string.Join(" ", reasons), endpoints.GetValueOrDefault(p.Pid, "No TCP endpoint observed")));
        }

        foreach (var id in _seen.Keys.Where(id => now - _seen[id].Last > TimeSpan.FromMinutes(2)).ToList())
        { _seen.Remove(id); _previousMemory.Remove(id); }

        var ordered = findings.OrderByDescending(x => x.RiskScore).ThenByDescending(x => x.MemoryMb).ToList();
        var report = new
        {
            schema = "performance-threat-report-v1", capturedAtUtc = now, machine = Environment.MachineName,
            note = "Heuristic triage only. Scores are not proof of compromise; verify evidence and use trusted security tools.",
            processes = ordered
        };
        string path = Path.Combine(_reportRoot, $"threat-snapshot-{now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return ordered;
    }

    public static string DefaultReportFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Performance", "Reports");

    private static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        try { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
