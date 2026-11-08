using GameBoost.Infrastructure;

namespace GameBoost.Services;

/// <summary>A group of files that can be cleaned up (e.g. temporary files or shader caches).</summary>
public sealed class CleanupCategory : ObservableObject
{
    private bool _isSelected;
    private long _sizeBytes;
    private int _fileCount;

    public CleanupCategory(string id, IReadOnlyList<string> roots, TimeSpan minAge, bool selected)
    {
        Id = id;
        Roots = roots;
        MinAge = minAge;
        _isSelected = selected;
    }

    public string Id { get; }
    public IReadOnlyList<string> Roots { get; }
    public TimeSpan MinAge { get; }
    public string Title => Loc.T($"Clean.{Id}.Title");
    public string Description => Loc.T($"Clean.{Id}.Desc");

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public long SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (SetProperty(ref _sizeBytes, value)) OnPropertyChanged(nameof(SizeText));
        }
    }

    public int FileCount { get => _fileCount; set => SetProperty(ref _fileCount, value); }

    public string SizeText => TempCleaner.FormatSize(SizeBytes);
}

/// <summary>Finds and deletes temporary files, caches and old crash reports of the current user.</summary>
public static class TempCleaner
{
    public static List<CleanupCategory> CreateCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string L(params string[] parts) => Path.Combine(new[] { local }.Concat(parts).ToArray());

        return new List<CleanupCategory>
        {
            new("Temp", new[] { Path.GetTempPath() }, TimeSpan.FromHours(24), true),
            new("CrashReports", new[] { L("CrashDumps"), L("Microsoft", "Windows", "WER", "ReportArchive"), L("Microsoft", "Windows", "WER", "ReportQueue") }, TimeSpan.Zero, true),
            new("Logs", new[] { AppPaths.LogDirectory }, TimeSpan.FromDays(7), true),
            new("DirectXCache", new[] { L("D3DSCache") }, TimeSpan.Zero, false),
            new("GpuCache", new[] { L("NVIDIA", "DXCache"), L("NVIDIA", "GLCache"), L("AMD", "DxCache"), L("AMD", "DxcCache"), L("AMD", "VkCache") }, TimeSpan.Zero, false)
        };
    }

    public static void Scan(CleanupCategory category)
    {
        long size = 0;
        var count = 0;
        foreach (var file in Files(category))
        {
            try
            {
                size += file.Length;
                count++;
            }
            catch
            {
                // vanished meanwhile
            }
        }
        category.SizeBytes = size;
        category.FileCount = count;
    }

    /// <summary>Deletes the files of a category. Files in use are skipped. Returns (deleted files, freed bytes).</summary>
    public static (int Files, long Bytes) Clean(CleanupCategory category)
    {
        var files = 0;
        long bytes = 0;
        foreach (var file in Files(category).ToList())
        {
            try
            {
                var len = file.Length;
                file.Attributes = FileAttributes.Normal;
                file.Delete();
                files++;
                bytes += len;
            }
            catch
            {
                // in use or no permission – skip
            }
        }

        foreach (var root in category.Roots.Where(Directory.Exists))
        {
            foreach (var dir in SafeDirectories(root).OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }
                catch
                {
                    // in use – keep
                }
            }
        }

        Logger.Info($"Cleanup {category.Id}: {files} files, {FormatSize(bytes)}");
        return (files, bytes);
    }

    private static IEnumerable<FileInfo> Files(CleanupCategory category)
    {
        var limit = DateTime.Now - category.MinAge;
        foreach (var root in category.Roots)
        {
            if (!Directory.Exists(root)) continue;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            };
            IEnumerable<FileInfo> files;
            try
            {
                files = new DirectoryInfo(root).EnumerateFiles("*", options);
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                DateTime written;
                try { written = f.LastWriteTime; }
                catch { continue; }
                if (category.MinAge > TimeSpan.Zero && written > limit) continue;
                yield return f;
            }
        }
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024 / 1024:0.0} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / 1024d / 1024:0} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:0} KB";
        return $"{bytes} B";
    }
}
