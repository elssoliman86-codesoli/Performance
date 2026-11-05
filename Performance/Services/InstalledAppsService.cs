using System.IO;
using Microsoft.Win32;
using System.Diagnostics;

namespace Performance.Services;

public sealed class InstalledAppsService
{
    public IReadOnlyList<InstalledAppRow> GetInstalledApps()
    {
        var items = new Dictionary<string, InstalledAppRow>(StringComparer.OrdinalIgnoreCase);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, items);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, items);
        ReadHive(RegistryHive.CurrentUser, RegistryView.Default, items);
        return items.Values.OrderBy(x => x.DisplayName).ToList();
    }

    private static void ReadHive(RegistryHive hive, RegistryView view, Dictionary<string, InstalledAppRow> target)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) return;
            foreach (string subName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var key = uninstall.OpenSubKey(subName);
                    if (key is null || Convert.ToInt32(key.GetValue("SystemComponent", 0)) == 1) continue;
                    string name = key.GetValue("DisplayName")?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string publisher = key.GetValue("Publisher")?.ToString() ?? "";
                    string version = key.GetValue("DisplayVersion")?.ToString() ?? "";
                    string location = key.GetValue("InstallLocation")?.ToString() ?? "";
                    string uninstallString = key.GetValue("UninstallString")?.ToString() ?? "";
                    string quiet = key.GetValue("QuietUninstallString")?.ToString() ?? "";
                    double size = 0;
                    if (double.TryParse(key.GetValue("EstimatedSize")?.ToString(), out var kb)) size = kb / 1024d;
                    if (!target.ContainsKey(name))
                        target[name] = new InstalledAppRow(name, publisher, version, location, uninstallString, quiet, size);
                }
                catch { }
            }
        }
        catch { }
    }

    public static bool LaunchUninstaller(InstalledAppRow app)
    {
        string command = !string.IsNullOrWhiteSpace(app.QuietUninstallString) ? app.QuietUninstallString : app.UninstallString;
        if (string.IsNullOrWhiteSpace(command)) return false;
        try
        {
            var info = new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };
            Process.Start(info);
            return true;
        }
        catch { return false; }
    }

    public static IReadOnlyList<ResidualFolderRow> FindResidualFolders(string appName)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        string needle = Normalize(appName);
        if (needle.Length < 4) return Array.Empty<ResidualFolderRow>();
        var result = new List<ResidualFolderRow>();
        foreach (var root in roots)
        {
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Normalize(Path.GetFileName(folder));
                    if (name.Length < 4 || !(name.Contains(needle, StringComparison.OrdinalIgnoreCase) || needle.Contains(name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    double size = GetDirectoryBytes(folder) / 1024d / 1024d;
                    result.Add(new ResidualFolderRow(folder, size));
                }
            }
            catch { }
        }
        return result.OrderByDescending(x => x.SizeMb).ToList();
    }

    public static bool DeleteResidualFolder(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string[] roots =
            {
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            };
            bool allowed = roots.Any(root => full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!allowed || !Directory.Exists(full)) return false;
            var info = new DirectoryInfo(full);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            Directory.Delete(full, recursive: true);
            return true;
        }
        catch { return false; }
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static long GetDirectoryBytes(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
                try { size += new FileInfo(file).Length; } catch { }
        }
        catch { }
        return size;
    }
}
