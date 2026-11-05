namespace Performance.Services;

public sealed class StorageScanner
{
    public Task<StorageScanResult> ScanAsync(string root, IProgress<(int Files, long Bytes)>? progress, CancellationToken token) =>
        Task.Run(() => Scan(root, progress, token), token);

    private static StorageScanResult Scan(string root, IProgress<(int Files, long Bytes)>? progress, CancellationToken token)
    {
        long total = 0;
        int count = 0;
        var category = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var largest = new PriorityQueue<StorageFileRow, double>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                long bytes = info.Length;
                total += bytes;
                count++;
                string group = Categorize(info.Extension, info.FullName);
                category[group] = category.GetValueOrDefault(group) + bytes;
                var row = new StorageFileRow(info.Name, info.FullName, group, bytes / 1024d / 1024d, info.LastWriteTime);
                largest.Enqueue(row, row.SizeMb);
                if (largest.Count > 160) largest.Dequeue();
                if (count % 400 == 0) progress?.Report((count, total));
            }
            catch { }
        }
        progress?.Report((count, total));
        var sorted = largest.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.SizeMb).ToList();
        return new StorageScanResult(total, count, sorted,
            category.ToDictionary(x => x.Key, x => x.Value / 1024d / 1024d));
    }

    private static string Categorize(string ext, string path)
    {
        string e = ext.ToLowerInvariant();
        if (new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".heic", ".tif" }.Contains(e)) return "Images";
        if (new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v" }.Contains(e)) return "Videos";
        if (new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a" }.Contains(e)) return "Audio";
        if (new[] { ".zip", ".7z", ".rar", ".iso", ".tar", ".gz" }.Contains(e)) return "Archives";
        if (new[] { ".exe", ".msi", ".msix", ".appx", ".cab" }.Contains(e)) return "Installers";
        if (new[] { ".dll", ".sys", ".dat", ".pak", ".bin" }.Contains(e) || path.Contains("SteamLibrary", StringComparison.OrdinalIgnoreCase)
            || path.Contains("XboxGames", StringComparison.OrdinalIgnoreCase)) return "Games & Apps";
        return "Documents & Other";
    }
}
