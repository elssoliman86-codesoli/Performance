using System.IO;
using System.Diagnostics;
using System.Security.AccessControl;
using Microsoft.Win32;

namespace Performance.Services;

public sealed record DeepDeleteCandidate(string Path, string Kind, double SizeMb, string Risk, string Details);

public static class DeepDeleteService
{
    public static IReadOnlyList<DeepDeleteCandidate> FindCandidates(InstalledAppRow app)
    {
        var found = new Dictionary<string, DeepDeleteCandidate>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string kind, string details)
        {
            try
            {
                string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
                if (!Directory.Exists(full) && !File.Exists(full)) return;
                if (IsProtected(full)) return;
                long bytes = Directory.Exists(full) ? DirectoryBytes(full) : new FileInfo(full).Length;
                found[full] = new(full, kind, bytes / 1048576d, "Review", details);
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(app.InstallLocation)) Add(app.InstallLocation, "Install location", "Registered install path; confirm the vendor uninstaller completed.");
        foreach (var root in new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                {
                    string leaf = Path.GetFileName(dir);
                    if (Matches(leaf, app.DisplayName) || (!string.IsNullOrWhiteSpace(app.Publisher) && Matches(leaf, app.Publisher)))
                        Add(dir, "Related folder candidate", "Name matches application or publisher; verify ownership before deleting.");
                }
            }
            catch { }
        }
        return found.Values.OrderByDescending(x => x.SizeMb).ToList();
    }

    public static IReadOnlyList<string> FindLockingProcesses(string path)
    {
        var result = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string exe = p.MainModule?.FileName ?? "";
                if (!string.IsNullOrWhiteSpace(exe) && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    result.Add($"{p.ProcessName} (PID {p.Id}) — {exe}");
            }
            catch { }
            finally { p.Dispose(); }
        }
        return result;
    }

    public static bool DeleteReviewedCandidate(string path, string expectedRoot)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || IsProtected(full)) return false;
            if (Directory.Exists(full))
            {
                if ((new DirectoryInfo(full).Attributes & FileAttributes.ReparsePoint) != 0) return false;
                Directory.Delete(full, true);
            }
            else if (File.Exists(full)) File.Delete(full);
            else return false;
            return true;
        }
        catch { return false; }
    }

    private static bool Matches(string value, string app)
    {
        string a = Normalize(value), b = Normalize(app);
        return a.Length >= 4 && b.Length >= 4 && (a.Contains(b) || b.Contains(a));
    }
    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool IsProtected(string path)
    {
        string[] blocked = { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.System), Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) };
        return blocked.Any(x => !string.IsNullOrWhiteSpace(x) && (string.Equals(Path.GetFullPath(path), Path.GetFullPath(x), StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path).StartsWith(Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
    }
    private static long DirectoryBytes(string path)
    {
        long total=0;
        foreach(var f in Directory.EnumerateFiles(path,"*",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint}))
            try{total+=new FileInfo(f).Length;}catch{}
        return total;
    }
}
