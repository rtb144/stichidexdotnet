using System.Text;
using System.Text.RegularExpressions;
using Stitchidex.Core.Models;
namespace Stitchidex.Core.Parsers;

public interface IEmbroideryParser
{
    string[] Extensions { get; }
    EmbroideryPattern Parse(Stream stream, string fileName);
}

internal sealed class StitchBuilder
{
    public readonly EmbroideryPattern P;
    double _x, _y; int _color;
    public StitchBuilder(EmbroideryPattern p) => P = p;
    public void Add(double dx, double dy, EmbroideryCommand c)
    {
        _x += dx; _y += dy;
        P.Stitches.Add(new StitchPoint(_x, _y, c, _color));
    }
    public void ColorChange() { P.Stitches.Add(new StitchPoint(_x, _y, EmbroideryCommand.ColorChange, _color)); _color++; }
    public void End() => P.Stitches.Add(new StitchPoint(_x, _y, EmbroideryCommand.End, _color));
}

internal static class ParserUtil
{
    public static string TitleFromFile(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        stem = Regex.Replace(stem, @"[_\-\.]+", " ").Trim();
        return string.IsNullOrEmpty(stem) ? "Untitled" : stem;
    }
    public static EmbroideryPattern New(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return new EmbroideryPattern
        {
            Title = TitleFromFile(fileName), FilePath = fileName,
            FormatFamily = EmbroideryParserRegistry.FormatFamilyMap.TryGetValue(ext, out var f) ? f : ext,
        };
    }
    public static byte[] ReadAll(Stream s) { using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
}

/// <summary>Brother PES (and raw PEC) parser.</summary>
public sealed class PesParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".pes", ".pec" };

    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var data = ParserUtil.ReadAll(stream);
        var p = ParserUtil.New(fileName);
        int pec;
        if (data.Length >= 12 && Encoding.ASCII.GetString(data, 0, 4) == "#PES")
        {
            pec = BitConverter.ToInt32(data, 8);
            p.Tags.Add("pes:" + Encoding.ASCII.GetString(data, 4, 4));
        }
        else if (data.Length >= 8 && Encoding.ASCII.GetString(data, 0, 8) == "#PEC0001") pec = 8;
        else throw new InvalidDataException("Not a PES/PEC file");
        if (pec < 0 || pec + 532 > data.Length) throw new InvalidDataException("PEC offset out of range");
        DecodePec(data, pec, p);
        return p;
    }

    internal static void DecodePec(byte[] d, int pec, EmbroideryPattern p)
    {
        string label = "";
        if (Encoding.ASCII.GetString(d, pec, 3) == "LA:")
            label = Encoding.ASCII.GetString(d, pec + 3, 16).Trim(' ', '\0');
        if (label.Length > 0)
        {
            p.Tags.Add("pec:" + label);
            // Prefer descriptive filename; fall back to PEC label when filename is generic.
            if (p.Title.Length < 3 || Regex.IsMatch(p.Title, @"^(design|untitled|\d+)$", RegexOptions.IgnoreCase)) p.Title = label;
        }
        int n = d[pec + 48] + 1;
        for (int i = 0; i < n && pec + 49 + i < d.Length; i++) p.Colors.Add(ThreadColor.FromPecIndex(d[pec + 49 + i]));
        var b = new StitchBuilder(p);
        int o = pec + 512 + 20;
        while (o < d.Length)
        {
            byte v1 = d[o];
            if (v1 == 0xFF) { b.End(); break; }
            if (v1 == 0xFE && o + 2 < d.Length && d[o + 1] == 0xB0) { b.ColorChange(); o += 3; continue; }
            var cmd = EmbroideryCommand.Stitch;
            int x = ReadCoord(d, ref o, ref cmd);
            int y = ReadCoord(d, ref o, ref cmd);
            b.Add(x, y, cmd);
        }
        p.ComputeStats();
    }

    static int ReadCoord(byte[] d, ref int o, ref EmbroideryCommand cmd)
    {
        if (o >= d.Length) return 0;
        byte v = d[o++];
        if ((v & 0x80) != 0)
        {
            if ((v & 0x20) != 0) cmd = EmbroideryCommand.Trim;
            else if ((v & 0x10) != 0 && cmd != EmbroideryCommand.Trim) cmd = EmbroideryCommand.Jump;
            int code = ((v & 0x0F) << 8) | (o < d.Length ? d[o++] : 0);
            return code > 0x7FF ? code - 0x1000 : code;
        }
        return v > 0x3F ? v - 0x80 : v;
    }
}

/// <summary>Tajima DST parser.</summary>
public sealed class DstParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".dst" };
    static int Bit(int b, int n) => (b >> n) & 1;

    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        if (d.Length < 512) throw new InvalidDataException("DST header too short");
        var p = ParserUtil.New(fileName);
        var hdr = Encoding.ASCII.GetString(d, 0, 512);
        var m = Regex.Match(hdr, @"LA:([^\r\n\x1a]{0,16})");
        if (m.Success && m.Groups[1].Value.Trim().Length > 0) p.Tags.Add("dst:" + m.Groups[1].Value.Trim());
        var b = new StitchBuilder(p);
        for (int o = 512; o + 2 < d.Length; o += 3)
        {
            int b0 = d[o], b1 = d[o + 1], b2 = d[o + 2];
            if (b2 == 0xF3) { b.End(); break; }
            int dx = Bit(b0,0) - Bit(b0,1) + 9*Bit(b0,2) - 9*Bit(b0,3) + 3*Bit(b1,0) - 3*Bit(b1,1) + 27*Bit(b1,2) - 27*Bit(b1,3) + 81*Bit(b2,2) - 81*Bit(b2,3);
            int dy = Bit(b0,7) - Bit(b0,6) + 9*Bit(b0,5) - 9*Bit(b0,4) + 3*Bit(b1,7) - 3*Bit(b1,6) + 27*Bit(b1,5) - 27*Bit(b1,4) + 81*Bit(b2,5) - 81*Bit(b2,4);
            if ((b2 & 0xC3) == 0xC3) { b.Add(dx, -dy, EmbroideryCommand.Jump); b.ColorChange(); }
            else if ((b2 & 0x80) != 0) b.Add(dx, -dy, EmbroideryCommand.Jump);
            else b.Add(dx, -dy, EmbroideryCommand.Stitch);
        }
        int colors = p.Stitches.Count(s => s.Command == EmbroideryCommand.ColorChange) + 1;
        for (int i = 0; i < colors; i++) p.Colors.Add(ThreadColor.FromPecIndex(1 + i % 64));
        p.ComputeStats();
        return p;
    }
}

/// <summary>Melco EXP parser.</summary>
public sealed class ExpParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".exp" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        var p = ParserUtil.New(fileName);
        var b = new StitchBuilder(p);
        int o = 0;
        while (o + 1 < d.Length)
        {
            byte x = d[o], y = d[o + 1]; o += 2;
            if (x == 0x80)
            {
                if (o + 1 >= d.Length) break;
                sbyte nx = (sbyte)d[o], ny = (sbyte)d[o + 1]; o += 2;
                if ((y & 0x01) != 0) { b.ColorChange(); b.Add(nx, -ny, EmbroideryCommand.Jump); }
                else if (y == 0x80) b.Add(nx, -ny, EmbroideryCommand.Trim);
                else if (y == 0x02 || y == 0x04) b.Add(nx, -ny, EmbroideryCommand.Jump);
                else b.Add(nx, -ny, EmbroideryCommand.Stitch);
                continue;
            }
            b.Add((sbyte)x, -(sbyte)y, EmbroideryCommand.Stitch);
        }
        b.End();
        int colors = p.Stitches.Count(s => s.Command == EmbroideryCommand.ColorChange) + 1;
        for (int i = 0; i < colors; i++) p.Colors.Add(ThreadColor.FromPecIndex(1 + i % 64));
        p.ComputeStats();
        return p;
    }
}

/// <summary>Janome JEF parser.</summary>
public sealed class JefParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".jef", ".jef+" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        if (d.Length < 0x74) throw new InvalidDataException("JEF header too short");
        var p = ParserUtil.New(fileName);
        int off = BitConverter.ToInt32(d, 0x00);
        int threads = BitConverter.ToInt32(d, 0x18);
        if (off < 0x74 || off > d.Length || threads < 0 || threads > 256) throw new InvalidDataException("Bad JEF header");
        for (int i = 0; i < threads && 0x74 + i * 4 + 3 < d.Length; i++)
            p.Colors.Add(ThreadColor.FromJanomeIndex(BitConverter.ToInt32(d, 0x74 + i * 4)));
        var b = new StitchBuilder(p);
        int o = off;
        while (o + 1 < d.Length)
        {
            byte x = d[o], y = d[o + 1]; o += 2;
            if (x == 0x80)
            {
                if (y == 0x10) break;
                if (o + 1 >= d.Length) break;
                sbyte nx = (sbyte)d[o], ny = (sbyte)d[o + 1]; o += 2;
                if (y == 0x01) { b.ColorChange(); b.Add(nx, -ny, EmbroideryCommand.Jump); }
                else b.Add(nx, -ny, EmbroideryCommand.Jump);
                continue;
            }
            b.Add((sbyte)x, -(sbyte)y, EmbroideryCommand.Stitch);
        }
        b.End();
        p.ComputeStats();
        return p;
    }
}

/// <summary>Singer XXX best-effort parser (stitch data at 0x100).</summary>
public sealed class XxxParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".xxx" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        var p = ParserUtil.New(fileName);
        var b = new StitchBuilder(p);
        int o = 0x100;
        while (o + 1 < d.Length)
        {
            byte x = d[o], y = d[o + 1]; o += 2;
            if (x == 0x7F)
            {
                if (y == 0x7F || o + 1 >= d.Length) break;
                sbyte nx = (sbyte)d[o], ny = (sbyte)d[o + 1]; o += 2;
                if (y == 0x08 || (y >= 0x0A && y <= 0x17)) { b.ColorChange(); b.Add(nx, -ny, EmbroideryCommand.Jump); }
                else if (y == 0x03) b.Add(nx, -ny, EmbroideryCommand.Trim);
                else b.Add(nx, -ny, EmbroideryCommand.Jump);
                continue;
            }
            b.Add((sbyte)x, -(sbyte)y, EmbroideryCommand.Stitch);
        }
        b.End();
        int colors = p.Stitches.Count(s => s.Command == EmbroideryCommand.ColorChange) + 1;
        for (int i = 0; i < colors; i++) p.Colors.Add(ThreadColor.FromPecIndex(1 + i % 64));
        p.ComputeStats();
        return p;
    }
}

/// <summary>Header-only parser for formats without full stitch decoding yet (VP3, HUS, VIP, SEW, PCS ...).</summary>
public sealed class HeaderOnlyParser : IEmbroideryParser
{
    readonly string[] _exts;
    public HeaderOnlyParser(params string[] exts) => _exts = exts;
    public string[] Extensions => _exts;
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        var p = ParserUtil.New(fileName);
        if (Path.GetExtension(fileName).Equals(".vp3", StringComparison.OrdinalIgnoreCase)
            && !(d.Length >= 5 && Encoding.ASCII.GetString(d, 0, 5) == "%vsm%"))
            throw new InvalidDataException("Not a VP3 file");
        p.Tags.Add("header-only");
        p.ComputeStats();
        return p;
    }
}

public static class EmbroideryParserRegistry
{
    public static readonly IReadOnlyDictionary<string, string> FormatFamilyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pes"] = "Brother / Baby Lock / Bernina (.pes)", [".pec"] = "Brother PEC Stitch (.pec)",
        [".dst"] = "Tajima Industrial Stitch (.dst)", [".exp"] = "Melco / Bernina Expanded (.exp)",
        [".jef"] = "Janome / Elna (.jef)", [".jef+"] = "Janome Extended (.jef+)",
        [".vp3"] = "Husqvarna Viking / Pfaff (.vp3)", [".vp4"] = "Husqvarna Viking / Pfaff v4 (.vp4)",
        [".vip"] = "Pfaff / Husqvarna VIP (.vip)", [".hus"] = "Husqvarna Viking (.hus)",
        [".shv"] = "Husqvarna Viking Designer 1 (.shv)", [".pcs"] = "Pfaff Home (.pcs)",
        [".pcm"] = "Pfaff PCM (.pcm)", [".pcq"] = "Pfaff Maxi (.pcq)", [".pcd"] = "Pfaff PCD (.pcd)",
        [".sew"] = "Janome / New Home (.sew)", [".xxx"] = "Singer / Compucon (.xxx)",
        [".u01"] = "Barudan Industrial (.u01)", [".dsb"] = "Barudan / Tajima (.dsb)", [".dsz"] = "ZSK / Tajima (.dsz)",
        [".tap"] = "Happy Industrial (.tap)", [".ksm"] = "Pfaff / Bernina KSM (.ksm)", [".csd"] = "Poem / Huskygram / Singer (.csd)",
        [".emb"] = "Wilcom Embroidery Studio OLE2 (.emb)", [".des"] = "Bernina Artista / Melco Design (.des)",
    };

    static readonly IEmbroideryParser[] Parsers =
    {
        new PesParser(), new DstParser(), new ExpParser(), new JefParser(), new XxxParser(),
        new HeaderOnlyParser(".vp3", ".vp4", ".hus", ".vip", ".shv", ".sew", ".pcs", ".pcm", ".pcq", ".pcd",
            ".u01", ".dsb", ".dsz", ".tap", ".ksm", ".csd", ".emb", ".des"),
    };

    public static bool IsEmbroideryExtension(string ext) => FormatFamilyMap.ContainsKey(ext);

    public static bool TryGetParser(string ext, out IEmbroideryParser? parser)
    {
        parser = Parsers.FirstOrDefault(p => p.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
        return parser != null;
    }

    public static EmbroideryPattern ParseFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!TryGetParser(ext, out var parser)) throw new NotSupportedException($"Unsupported extension '{ext}'");
        using var fs = File.OpenRead(path);
        var p = parser!.Parse(fs, path);
        p.FilePath = path;
        return p;
    }
}
