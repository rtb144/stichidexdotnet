using System.Runtime.InteropServices;
using System.Text;
using Stitchidex.Core.Parsers;
namespace Stitchidex.Core.Services;

public sealed record VolumeInfo(bool IsRemovable, string RootPath, string VolumeLabel, string VolumeSerial, string VolumeRelativePath);
public sealed record ResolvedSource(string? Path, string Status, bool FromOfflineCache);

/// <summary>Removable-drive resilience: volume identity, drive-letter remapping, offline cache, USB→computer backup.</summary>
public static class UsbDriveService
{
    public const long MaxCachedBytes = 2 * 1024 * 1024;
    static readonly HashSet<string> Sidecars = new(StringComparer.OrdinalIgnoreCase) { ".edr", ".col", ".inf" };

    /// <summary>Overrides real volume detection (tests / simulations).</summary>
    public static Func<IEnumerable<(string RootPath, string VolumeLabel, string VolumeSerial)>>? VolumeProvider { get; set; }

    public static string OfflineCacheDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stitchidex", "offline_cache");

    /// <summary>Opens a file without locking it (safe for USB sticks).</summary>
    public static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    public static IReadOnlyList<(string RootPath, string VolumeLabel, string VolumeSerial)> GetMountedVolumes()
    {
        if (VolumeProvider != null) return VolumeProvider().ToList();
        var list = new List<(string, string, string)>();
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady))
                    list.Add((d.RootDirectory.FullName, d.VolumeLabel ?? "", WindowsSerial(d.RootDirectory.FullName)));
                return list;
            }
            var uuids = LinuxUuids();
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var p = line.Split(' ');
                if (p.Length < 2) continue;
                var mp = p[1].Replace("\\040", " ");
                if (!(mp.StartsWith("/media/") || mp.StartsWith("/run/media/") || mp.StartsWith("/mnt/"))) continue;
                uuids.TryGetValue(p[0], out var uuid);
                list.Add((mp, Path.GetFileName(mp), uuid ?? ""));
            }
        }
        catch (Exception) { }
        return list;
    }

    static Dictionary<string, string> LinuxUuids()
    {
        var d = new Dictionary<string, string>();
        try
        {
            foreach (var f in Directory.GetFiles("/dev/disk/by-uuid"))
            {
                var target = new FileInfo(f).ResolveLinkTarget(true)?.FullName;
                if (target != null) d[target] = Path.GetFileName(f);
            }
        }
        catch (Exception) { }
        return d;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeInformationW(string root, StringBuilder? name, int nameSize, out uint serial, out uint maxLen, out uint flags, StringBuilder? fs, int fsSize);

    static string WindowsSerial(string root)
    {
        try { return GetVolumeInformationW(root, null, 0, out var s, out _, out _, null, 0) ? s.ToString("X8") : ""; }
        catch (Exception) { return ""; }
    }

    /// <summary>Is <paramref name="filePath"/> on a removable volume? Returns label/serial/relative path.</summary>
    public static VolumeInfo InspectPathVolume(string filePath, IEnumerable<(string RootPath, string VolumeLabel, string VolumeSerial)>? mountedVolumes = null)
    {
        var full = Path.GetFullPath(filePath);
        var vols = (mountedVolumes ?? GetMountedVolumes()).OrderByDescending(v => v.RootPath.Length);
        foreach (var v in vols)
        {
            var root = Path.GetFullPath(v.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return new VolumeInfo(true, v.RootPath, string.IsNullOrEmpty(v.VolumeLabel) ? Path.GetFileName(root) : v.VolumeLabel, v.VolumeSerial,
                    Path.GetRelativePath(root, full));
        }
        bool linuxRemovable = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && mountedVolumes == null && VolumeProvider == null &&
                              (full.StartsWith("/media/") || full.StartsWith("/run/media/"));
        return new VolumeInfo(linuxRemovable, "", "", "", "");
    }

    /// <summary>Finds a usable source file: original path → same volume at a new drive letter → offline cache.</summary>
    public static ResolvedSource ResolvePatternSourcePath(CatalogEntry record, SqliteCatalogService? catalog = null,
        IEnumerable<(string RootPath, string VolumeLabel, string VolumeSerial)>? mountedVolumes = null)
    {
        if (!string.IsNullOrEmpty(record.Path) && File.Exists(record.Path)) return new(record.Path, "Connected", false);
        if (!string.IsNullOrEmpty(record.VolumeRelativePath))
            foreach (var v in mountedVolumes ?? GetMountedVolumes())
            {
                bool match = !string.IsNullOrEmpty(record.VolumeSerial) ? string.Equals(v.VolumeSerial, record.VolumeSerial, StringComparison.OrdinalIgnoreCase)
                                                                           : string.Equals(v.VolumeLabel, record.VolumeLabel, StringComparison.OrdinalIgnoreCase);
                var cand = Path.Combine(v.RootPath, record.VolumeRelativePath);
                if (match && File.Exists(cand)) return new(cand, $"Connected ({v.VolumeLabel} at {v.RootPath})", false);
            }
        var bytes = catalog?.GetCachedPatternBytes(record.Id);
        if (bytes is { Length: > 0 })
        {
            var dir = Path.Combine(OfflineCacheDir, record.Id.ToString());
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, Path.GetFileName(record.Path));
            File.WriteAllBytes(f, bytes);
            return new(f, $"USB '{record.VolumeLabel}' Unplugged — Ready from Offline Cache", true);
        }
        return new(null, $"USB '{record.VolumeLabel}' Unplugged — Insert '{record.VolumeLabel}' to export", false);
    }

    /// <summary>Copies a USB folder's embroidery files (+ .edr/.col/.inf sidecars) to Documents/Stitchidex Library/&lt;Folder&gt;.</summary>
    public static string CopyFolderPatternsToLocalLibrary(string usbSourceDir, string? customLibraryRoot = null,
        IProgress<(int Done, int Total, string CurrentFile)>? progress = null, CancellationToken ct = default)
    {
        var src = Path.GetFullPath(usbSourceDir).TrimEnd(Path.DirectorySeparatorChar);
        var libRoot = customLibraryRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Stitchidex Library");
        var name = Path.GetFileName(src);
        if (string.IsNullOrEmpty(name)) name = InspectPathVolume(src).VolumeLabel is { Length: > 0 } l ? l : "USB Patterns";
        var dest = Path.Combine(libRoot, name);
        var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => Sidecars.Contains(Path.GetExtension(f)) || EmbroideryParserRegistry.IsEmbroideryExtension(Path.GetExtension(f))).ToList();
        int done = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(dest, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.WriteAllBytes(target, ReadAllBytesShared(f));
            progress?.Report((++done, files.Count, Path.GetFileName(f)));
        }
        return dest;
    }
}
