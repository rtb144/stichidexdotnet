using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
namespace Stitchidex.Core.Services;

public enum UsbDestination { Root, Embroidery, bPocket }
public sealed record RemovableDrive(string RootPath, string Label, string? DeviceNode);

public sealed class UsbExportService
{
    public Func<IEnumerable<string>> LinuxMediaRoots { get; set; } = () =>
    {
        var u = Environment.UserName;
        return new[] { $"/media/{u}", $"/run/media/{u}", "/media" };
    };

    public IReadOnlyList<RemovableDrive> DetectRemovableDrives()
    {
        var list = new List<RemovableDrive>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady))
                list.Add(new RemovableDrive(d.RootDirectory.FullName, string.IsNullOrEmpty(d.VolumeLabel) ? d.Name : d.VolumeLabel, null));
            return list;
        }
        var mounts = ReadMounts();
        foreach (var root in LinuxMediaRoots().Where(Directory.Exists))
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (list.Any(x => x.RootPath == dir)) continue;
                if (root == "/media" && LinuxMediaRoots().Contains(dir)) continue;
                mounts.TryGetValue(dir, out var dev);
                list.Add(new RemovableDrive(dir, Path.GetFileName(dir), dev));
            }
        return list;
    }

    static Dictionary<string, string> ReadMounts()
    {
        var m = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var p = line.Split(' ');
                if (p.Length > 1) m[p[1].Replace("\\040", " ")] = p[0];
            }
        }
        catch { }
        return m;
    }

    public static string SanitizeBrotherFilename(string name, int maxLen = 32)
    {
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        stem = Regex.Replace(stem, @"\s+", "_");
        stem = Regex.Replace(stem, @"[^A-Za-z0-9_-]", "");
        stem = Regex.Replace(stem, "_{2,}", "_").Trim('_', '-');
        if (stem.Length == 0) stem = "design";
        if (stem.Length > maxLen) stem = stem[..maxLen];
        return stem + ext.ToLowerInvariant();
    }

    public static string DestinationFolder(string driveRoot, UsbDestination dest) => dest switch
    {
        UsbDestination.Embroidery => Path.Combine(driveRoot, "Embroidery"),
        UsbDestination.bPocket => Path.Combine(driveRoot, "bPocket"),
        _ => driveRoot,
    };

    public static string GenerateThreadColorChartHtml(IEnumerable<(string Title, IEnumerable<Models.ThreadColor> Colors)> patterns)
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Thread Color Chart</title><style>body{font-family:sans-serif}" +
            "table{border-collapse:collapse;margin-bottom:18px}td,th{border:1px solid #999;padding:4px 8px}.sw{width:28px;height:18px;border:1px solid #333}" +
            "@media print{h2{page-break-after:avoid}}</style></head><body><h1>Stitchidex Thread Color Chart</h1>");
        foreach (var (title, colors) in patterns)
        {
            sb.Append($"<h2>{WebUtility.HtmlEncode(title)}</h2><table><tr><th>#</th><th>Swatch</th><th>Name</th><th>Hex</th></tr>");
            int i = 1;
            foreach (var c in colors)
                sb.Append($"<tr><td>{i++}</td><td><div class=\"sw\" style=\"background:{c.Hex}\"></div></td><td>{WebUtility.HtmlEncode(c.Name)}</td><td>{c.Hex}</td></tr>");
            sb.Append("</table>");
        }
        return sb.Append("</body></html>").ToString();
    }

    public IReadOnlyList<string> CopyToUsb(IEnumerable<string> files, string driveRoot, UsbDestination dest, string? chartHtml = null)
    {
        var folder = DestinationFolder(driveRoot, dest);
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        foreach (var f in files)
        {
            var name = SanitizeBrotherFilename(Path.GetFileName(f));
            var target = Path.Combine(folder, name);
            int n = 1;
            while (File.Exists(target)) target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}_{n++}{Path.GetExtension(name)}");
            File.Copy(f, target);
            written.Add(target);
        }
        if (chartHtml != null)
        {
            var chart = Path.Combine(folder, "thread_color_chart.html");
            File.WriteAllText(chart, chartHtml);
            written.Add(chart);
        }
        return written;
    }

    public static (string FileName, string Arguments)[] BuildEjectCommands(RemovableDrive drive, bool windows)
    {
        if (windows)
        {
            var letter = drive.RootPath.TrimEnd('\\', '/');
            var ps = $"(New-Object -ComObject Shell.Application).NameSpace(17).ParseName('{letter}').InvokeVerb('Eject')";
            return new[] { ("powershell", $"-NoProfile -Command \"{ps}\"") };
        }
        var cmds = new List<(string, string)>();
        if (drive.DeviceNode != null) cmds.Add(("udisksctl", $"unmount -b {drive.DeviceNode}"));
        cmds.Add(("gio", $"mount -u \"{drive.RootPath}\""));
        return cmds.ToArray();
    }

    public bool SafelyEject(RemovableDrive drive)
    {
        foreach (var (file, args) in BuildEjectCommands(drive, RuntimeInformation.IsOSPlatform(OSPlatform.Windows)))
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                if (p == null) continue;
                p.WaitForExit(15000);
                if (p.ExitCode == 0) return true;
            }
            catch { }
        }
        return false;
    }
}
