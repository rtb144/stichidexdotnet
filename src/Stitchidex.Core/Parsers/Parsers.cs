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


internal static class Palettes
{
    public static readonly string[] JefNames = {"Placeholder","Black","White","Yellow","Orange","Olive Green","Green","Sky","Purple","Pink","Red","Brown","Blue","Gold","Dark Brown","Pale Violet","Pale Yellow","Pale Pink","Peach","Beige","Wine Red","Pale Sky","Yellow Green","Silver Gray","Gray","Pale Aqua","Baby Blue","Powder Blue","Bright Blue","Slate Blue","Navy Blue","Salmon Pink","Coral","Burnt Orange","Cinnamon","Umber","Blond","Sunflower","Orchid Pink","Peony Purple","Burgundy","Royal Purple","Cardinal Red","Opal Green","Moss Green","Meadow Green","Dark Green","Aquamarine","Emerald Green","Peacock Green","Dark Gray","Ivory White","Hazel","Toast","Salmon","Cocoa Brown","Sienna","Sepia","Dark Sepia","Violet Blue","Blue Ink","Sola Blue","Green Dust","Crimson","Floral Pink","Wine","Olive Drab","Meadow","Mustard","Yellow Ocher","Old Gold","Honey Dew","Tangerine","Canary Yellow","Vermilion","Bright Green","Ocean Blue","Beige Gray","Bamboo"};
    public static readonly uint[] JefRgb = {0x000000,0x000000,0xFFFFFF,0xFFFF17,0xFF6600,0x2F5933,0x237336,0x65C2C8,0xAB5A96,0xF669A0,0xFF0000,0xB1704E,0x0B2F84,0xE4C35D,0x481A05,0xAC9CC7,0xFCF294,0xF999B7,0xFAB381,0xC9A480,0x970533,0xA0B8CC,0x7FC21C,0xE5E5E5,0x889B9B,0x98D6BD,0xB2E1E3,0x368BA0,0x4F83AB,0x386A91,0x071650,0xF999A2,0xF9676B,0xE3311F,0xE2A188,0xB59474,0xE4CF99,0xFFCB00,0xE1ADD4,0xC3007E,0x80004B,0x540571,0xB10525,0xCAE0C0,0x899856,0x5C941A,0x003114,0x5DAE94,0x4CBF8F,0x007772,0x595B61,0xFFFFF2,0xB15818,0xCB8A07,0x986C80,0x98692D,0x4D3419,0x4C330B,0x33200A,0x523A97,0x0D217E,0x1E77AC,0xB2DD53,0xF33689,0xDE649E,0x984161,0x4C5612,0x4C881F,0xE4DE79,0xCB8A1A,0xCBA21C,0xFF9805,0xFCB257,0xFFE505,0xF0331F,0x1A842D,0x386CAE,0xE3C4B4,0xE3AC81};
    public static readonly uint[] HusRgb = {0x000000,0x0000E7,0x00C600,0xFF0000,0x840084,0xFFFF00,0x848484,0x8484E7,0x00FF84,0xFF7B31,0xFF8CA5,0x845200,0xFFFFFF,0x000084,0x008400,0x7B0000,0xFF6384,0x522952,0xFF00FF,0xFFDE00,0xFFFF9C,0x525252,0xD6D6D6,0xFF5208,0xFF9C5A,0xFF52B5,0xFFC6DE,0x523100,0xB5A584};
    public static readonly uint[] SewRgb = {0x000000,0x000000,0xFFFFFF,0xFFFF17,0xFAA060,0x5C7649,0x40C030,0x65C2C8,0xAC80BE,0xF5BCCB,0xFF0000,0xC08000,0x0000F0,0xE4C35D,0xA52A2A,0xD5B0D4,0xFCF294,0xF0D0C0,0xFFC000,0xC9A480,0x9B3D4B,0xA0B8CC,0x7FC21C,0xB9B9B9,0xA0A0A0,0x98D6BD,0xB8F0F0,0x368BA0,0x4F83AB,0x386A91,0x00206B,0xE5C5CA,0xF9676B,0xE3311F,0xE2A188,0xB59474,0xE4CF99,0xE1CB00,0xE1ADD4,0xC3007E,0x80004B,0xA060B0,0xC04020,0xCAE0C0,0x899856,0x00AA00,0x218A21,0x5DAE94,0x4CBF8F,0x007772,0x707070,0xF2FFFF,0xB15818,0xCB8A07,0xF7927B,0x98692D,0xA27148,0x7B554A,0x4F3946,0x523A97,0x0000A0,0x0096DE,0xB2DD53,0xFA8FBB,0xDE649E,0xB55066,0x5E5747,0x4C881F,0xE4DC79,0xCB8A1A,0xC6AA42,0xECB02C,0xF88040,0xFFE505,0xFA7A7A,0x6BE000,0x386CAE,0xD0BAB0,0xE3BE81};

    /// <summary>Vibrant high-contrast studio cycle for formats with no embedded RGB palette.</summary>
    public static readonly uint[] Studio = {0x38BDF8,0xF43F5E,0x10B981,0xF59E0B,0xA855F7,0xEC4899,0x22D3EE,0x84CC16};
    public static ThreadColor Rgb(uint v, string? name = null) =>
        new((byte)(v >> 16), (byte)(v >> 8), (byte)v, name ?? $"#{v & 0xFFFFFF:X6}");
    public static ThreadColor StudioAt(int i) => Rgb(Studio[i % Studio.Length], $"Studio {i + 1}");

    /// <summary>Fill colors: sidecar (.edr/.col/.inf/.rgb) first, else studio cycle, so count == color blocks.</summary>
    public static void FillMissing(EmbroideryPattern p)
    {
        int blocks = p.Stitches.Count(s => s.Command == EmbroideryCommand.ColorChange) + 1;
        if (p.Colors.Count == 0) foreach (var c in Sidecar.Load(p.FilePath)) p.Colors.Add(c);
        for (int i = p.Colors.Count; i < blocks; i++) p.Colors.Add(StudioAt(i));
    }
}

/// <summary>Loads thread colors from sidecar files next to palette-less designs (pyembroidery Edr/Col/Inf readers).</summary>
public static class Sidecar
{
    public static List<ThreadColor> Load(string path)
    {
        var res = new List<ThreadColor>();
        if (string.IsNullOrEmpty(path)) return res;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(path);
            foreach (var ext in new[] { ".edr", ".EDR", ".col", ".COL", ".inf", ".INF" })
            {
                var f = Path.Combine(dir, stem + ext);
                if (!File.Exists(f)) continue;
                var d = File.ReadAllBytes(f);
                var e = ext.ToLowerInvariant();
                if (e == ".edr") for (int o = 0; o + 3 < d.Length; o += 4) res.Add(Palettes.Rgb((uint)(d[o] << 16 | d[o + 1] << 8 | d[o + 2])));
                else if (e == ".col")
                {
                    foreach (var line in Encoding.ASCII.GetString(d).Split('\n').Skip(1))
                    {
                        var parts = line.Trim().Split(',');
                        if (parts.Length >= 4 && byte.TryParse(parts[1], out var r) && byte.TryParse(parts[2], out var g) && byte.TryParse(parts[3], out var bb))
                            res.Add(new ThreadColor(r, g, bb, $"#{r:X2}{g:X2}{bb:X2}"));
                    }
                }
                else if (e == ".inf" && d.Length > 16)
                {
                    int n = (d[12] << 8) | d[13]; int o = 14;
                    for (int i = 0; i < n && o + 2 <= d.Length; i++)
                    {
                        int len = (d[o] << 8) | d[o + 1];
                        if (o + 10 <= d.Length) res.Add(new ThreadColor(d[o + 6], d[o + 7], d[o + 8], $"#{d[o + 6]:X2}{d[o + 7]:X2}{d[o + 8]:X2}"));
                        o += Math.Max(len, 2);
                    }
                }
                if (res.Count > 0) return res;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return res;
    }
}

internal static class Rd
{
    public static int S8(byte b) => (sbyte)b;
    public static int U16LE(byte[] d, int o) => o + 1 < d.Length ? d[o] | d[o + 1] << 8 : 0;
    public static int U16BE(byte[] d, int o) => o + 1 < d.Length ? d[o] << 8 | d[o + 1] : 0;
    public static int I32LE(byte[] d, int o) => o + 3 < d.Length ? BitConverter.ToInt32(d, o) : 0;
    public static int I32BE(byte[] d, int o) => o + 3 < d.Length ? d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3] : 0;
    public static int S24LE(byte[] d, int o) { int v = d[o] | d[o + 1] << 8 | d[o + 2] << 16; return v > 0x7FFFFF ? v - 0x1000000 : v; }
}

/// <summary>Tajima DST family: .dst/.tap use DST ternary, .dsb Barudan B-encoding, .dsz ZSK Z-encoding (pyembroidery Dst/Dsb/Dsz/TapReader).</summary>
public sealed class DstParser : IEmbroideryParser
{
    public enum Flavor { Dst, Dsb, Dsz, Tap }
    readonly Flavor _f;
    public DstParser() : this(Flavor.Dst) { }
    public DstParser(Flavor f) => _f = f;
    public string[] Extensions => _f switch { Flavor.Dsb => new[] { ".dsb" }, Flavor.Dsz => new[] { ".dsz" }, Flavor.Tap => new[] { ".tap" }, _ => new[] { ".dst" } };
    static int Bit(int b, int n) => (b >> n) & 1;

    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        int start = _f == Flavor.Tap ? 0 : 512;
        if (d.Length < start + 3) throw new InvalidDataException("DST-family file too short");
        var p = ParserUtil.New(fileName);
        if (start == 512)
        {
            var hdr = Encoding.Latin1.GetString(d, 0, 512);
            var m = Regex.Match(hdr, @"LA:([^\r\n\x1a]{0,16})");
            if (m.Success && m.Groups[1].Value.Trim().Length > 0) p.Tags.Add("dst:" + m.Groups[1].Value.Trim());
        }
        var b = new StitchBuilder(p);
        for (int o = start; o + 2 < d.Length; o += 3)
        {
            int b0 = d[o], b1 = d[o + 1], b2 = d[o + 2];
            if (_f == Flavor.Dsb || _f == Flavor.Dsz)
            {
                int ctrl, x, y;
                if (_f == Flavor.Dsb) { ctrl = b0; y = -b1; x = b2; if ((ctrl & 0x40) != 0) y = -y; if ((ctrl & 0x20) != 0) x = -x; }
                else { ctrl = b2; y = -b0; x = b1; if ((ctrl & 0x40) != 0) x = -x; if ((ctrl & 0x20) != 0) y = -y; }
                int c5 = ctrl & 0x1F;
                if (c5 == 0) { b.Add(x, y, EmbroideryCommand.Stitch); continue; }
                if (c5 == 1) { b.Add(x, y, EmbroideryCommand.Jump); continue; }
                if (_f == Flavor.Dsb)
                {
                    if (ctrl == 0xF8) break;
                    if (ctrl == 0xE7) { b.Add(0, 0, EmbroideryCommand.Trim); continue; }
                    if (ctrl >= 0xE8 && ctrl < 0xF8) { b.ColorChange(); continue; }
                }
                else
                {
                    if (ctrl == 0x9B) { b.Add(0, 0, EmbroideryCommand.Trim); continue; }
                    if (ctrl >= 0x82 && ctrl <= 0x9A) { b.ColorChange(); continue; }
                }
                break;
            }
            int dx = Bit(b0,0) - Bit(b0,1) + 9*Bit(b0,2) - 9*Bit(b0,3) + 3*Bit(b1,0) - 3*Bit(b1,1) + 27*Bit(b1,2) - 27*Bit(b1,3) + 81*Bit(b2,2) - 81*Bit(b2,3);
            int dy = Bit(b0,7) - Bit(b0,6) + 9*Bit(b0,5) - 9*Bit(b0,4) + 3*Bit(b1,7) - 3*Bit(b1,6) + 27*Bit(b1,5) - 27*Bit(b1,4) + 81*Bit(b2,5) - 81*Bit(b2,4);
            if ((b2 & 0xF3) == 0xF3) break;
            if ((b2 & 0xC3) == 0xC3) { b.Add(dx, -dy, EmbroideryCommand.Jump); b.ColorChange(); }
            else if ((b2 & 0x43) == 0x43) b.Add(dx, -dy, EmbroideryCommand.Jump); // sequin mode toggle
            else if ((b2 & 0x83) == 0x83) b.Add(dx, -dy, EmbroideryCommand.Jump);
            else b.Add(dx, -dy, EmbroideryCommand.Stitch);
        }
        b.End();
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Barudan U01 (pyembroidery U01Reader).</summary>
public sealed class U01Parser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".u01" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        var p = ParserUtil.New(fileName);
        var b = new StitchBuilder(p);
        for (int o = 0x100; o + 2 < d.Length; o += 3)
        {
            int ctrl = d[o], dy = -d[o + 1], dx = d[o + 2];
            if ((ctrl & 0x20) != 0) dx = -dx;
            if ((ctrl & 0x40) != 0) dy = -dy;
            int c = ctrl & 0x1F;
            bool mv = dx != 0 || dy != 0;
            if (c == 0 || c == 2 || c == 4) { if (c == 0 || mv) b.Add(dx, dy, EmbroideryCommand.Stitch); }
            else if (c == 1 || c == 3 || c == 5) { if (c == 1 || mv) b.Add(dx, dy, EmbroideryCommand.Jump); }
            else if (c == 6 || c == 7) { b.Add(0, 0, EmbroideryCommand.Trim); if (mv) b.Add(dx, dy, EmbroideryCommand.Jump); }
            else if (c >= 0x08 && c <= 0x17) { b.ColorChange(); if (mv) b.Add(dx, dy, EmbroideryCommand.Jump); }
            else break;
        }
        b.End();
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Melco EXP (pyembroidery ExpReader). Escapes: 0x80 ctrl + 2-byte dx/dy.</summary>
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
            if (x != 0x80) { b.Add(Rd.S8(x), -Rd.S8(y), EmbroideryCommand.Stitch); continue; }
            if (o + 1 >= d.Length) break;
            int nx = Rd.S8(d[o]), ny = -Rd.S8(d[o + 1]); o += 2;
            if (y == 0x80) b.Add(0, 0, EmbroideryCommand.Trim);
            else if (y == 0x02) b.Add(nx, ny, EmbroideryCommand.Stitch);
            else if (y == 0x04) b.Add(nx, ny, EmbroideryCommand.Jump);
            else if (y == 0x01) { b.ColorChange(); if (nx != 0 || ny != 0) b.Add(nx, ny, EmbroideryCommand.Jump); }
            else break;
        }
        b.End();
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Janome JEF (pyembroidery JefReader) with full 80-entry Janome palette.</summary>
public sealed class JefParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".jef", ".jef+" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        if (d.Length < 0x74) throw new InvalidDataException("JEF header too short");
        var p = ParserUtil.New(fileName);
        int off = Rd.I32LE(d, 0x00);
        int threads = Rd.I32LE(d, 0x18);
        if (off < 0x74 || off > d.Length || threads < 0 || threads > 256) throw new InvalidDataException("Bad JEF header");
        var list = new List<ThreadColor?>();
        for (int i = 0; i < threads && 0x74 + i * 4 + 3 < d.Length; i++)
        {
            int idx = Math.Abs(Rd.I32LE(d, 0x74 + i * 4));
            list.Add(idx == 0 ? null : Palettes.Rgb(Palettes.JefRgb[idx % Palettes.JefRgb.Length], Palettes.JefNames[idx % Palettes.JefNames.Length]));
        }
        var b = new StitchBuilder(p);
        int o = off;
        while (o + 1 < d.Length)
        {
            byte x = d[o], y = d[o + 1]; o += 2;
            if (x != 0x80) { b.Add(Rd.S8(x), -Rd.S8(y), EmbroideryCommand.Stitch); continue; }
            if (o + 1 >= d.Length) break;
            int nx = Rd.S8(d[o]), ny = -Rd.S8(d[o + 1]); o += 2;
            if (y == 0x02) b.Add(nx, ny, EmbroideryCommand.Jump);
            else if (y == 0x01) b.ColorChange();
            else break; // 0x10 = end
        }
        b.End();
        foreach (var c in list) if (c != null) p.Colors.Add(c);
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Janome/New Home SEW (pyembroidery SewReader).</summary>
public sealed class SewParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".sew" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        if (d.Length < 0x1D7A) throw new InvalidDataException("SEW file too short");
        var p = ParserUtil.New(fileName);
        int n = Rd.U16LE(d, 0);
        for (int i = 0; i < n && i < 256; i++)
        {
            int idx = Rd.U16LE(d, 2 + i * 2) % Palettes.SewRgb.Length;
            p.Colors.Add(Palettes.Rgb(Palettes.SewRgb[idx], $"Sew {idx}"));
        }
        var b = new StitchBuilder(p);
        int o = 0x1D78;
        while (o + 1 < d.Length)
        {
            byte x = d[o], y = d[o + 1]; o += 2;
            if (x != 0x80) { b.Add(Rd.S8(x), -Rd.S8(y), EmbroideryCommand.Stitch); continue; }
            if (o + 1 >= d.Length) break;
            int nx = Rd.S8(d[o]), ny = -Rd.S8(d[o + 1]); o += 2;
            if ((y & 1) != 0) b.ColorChange();
            else if (y == 0x04 || y == 0x02) b.Add(nx, ny, EmbroideryCommand.Jump);
            else if (y == 0x10) b.Add(nx, ny, EmbroideryCommand.Stitch);
            else break;
        }
        b.End();
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Pfaff PCS (pyembroidery PcsReader), absolute 24-bit coordinates.</summary>
public sealed class PcsParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".pcs" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        if (d.Length < 4) throw new InvalidDataException("PCS too short");
        var p = ParserUtil.New(fileName);
        int n = Rd.U16LE(d, 2), o = 4;
        for (int i = 0; i < n && o + 3 < d.Length; i++, o += 4) p.Colors.Add(Palettes.Rgb((uint)(d[o] << 16 | d[o + 1] << 8 | d[o + 2])));
        o += 2;
        var b = new StitchBuilder(p);
        double cx = 0, cy = 0; const double r = 5.0 / 3.0;
        for (; o + 8 < d.Length; o += 9)
        {
            double x = Rd.S24LE(d, o + 1) * r, y = -Rd.S24LE(d, o + 5) * r;
            int ctrl = d[o + 8];
            if (ctrl == 0) { b.Add(x - cx, y - cy, EmbroideryCommand.Stitch); cx = x; cy = y; }
            else if ((ctrl & 1) != 0) b.ColorChange();
            else if ((ctrl & 4) != 0) { b.Add(x - cx, y - cy, EmbroideryCommand.Jump); cx = x; cy = y; }
            else break;
        }
        b.End();
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Husqvarna Viking HUS (pyembroidery HusReader + EmbCompress).</summary>
public sealed class HusParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".hus" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        if (d.Length < 0x2A) throw new InvalidDataException("HUS too short");
        var p = ParserUtil.New(fileName);
        int nst = Rd.I32LE(d, 4), ncol = Rd.I32LE(d, 8);
        int cmdOff = Rd.I32LE(d, 0x14), xOff = Rd.I32LE(d, 0x18), yOff = Rd.I32LE(d, 0x1C);
        if (nst < 0 || ncol < 0 || ncol > 256 || cmdOff < 0 || xOff < cmdOff || yOff < xOff || yOff > d.Length) throw new InvalidDataException("Bad HUS header");
        for (int i = 0; i < ncol; i++)
        {
            int idx = Rd.U16LE(d, 0x2A + i * 2);
            p.Colors.Add(Palettes.Rgb(Palettes.HusRgb[idx % Palettes.HusRgb.Length], $"Hus {idx}"));
        }
        var cmd = EmbCompress.Expand(d[cmdOff..xOff], nst);
        var xs = EmbCompress.Expand(d[xOff..yOff], nst);
        var ys = EmbCompress.Expand(d[yOff..], nst);
        int n = Math.Min(cmd.Count, Math.Min(xs.Count, ys.Count));
        var b = new StitchBuilder(p);
        for (int i = 0; i < n; i++)
        {
            int c = cmd[i], x = (sbyte)xs[i], y = -(sbyte)ys[i];
            if (c == 0x80) b.Add(x, y, EmbroideryCommand.Stitch);
            else if (c == 0x81) b.Add(x, y, EmbroideryCommand.Jump);
            else if (c == 0x84) { b.Add(x, y, EmbroideryCommand.Jump); b.ColorChange(); }
            else if (c == 0x88) { if (x != 0 || y != 0) b.Add(x, y, EmbroideryCommand.Jump); b.Add(0, 0, EmbroideryCommand.Trim); }
            else break;
        }
        b.End();
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Port of pyembroidery EmbCompress (Huffman + LZ) used by HUS/VIP.</summary>
public sealed class EmbCompress
{
    sealed class Huffman
    {
        public int Default; public int[]? Lengths; int[]? _table; int _w;
        public Huffman(int[]? lengths, int def = 0) { Lengths = lengths; Default = def; }
        public void Build()
        {
            _w = Lengths!.Max(); var t = new List<int>(); double size = 1 << _w;
            for (int bl = 1; bl <= _w; bl++) { size /= 2.0; for (int i = 0; i < Lengths!.Length; i++) if (Lengths![i] == bl) for (int k = 0; k < (int)size; k++) t.Add(i); }
            _table = t.ToArray();
        }
        public (int V, int L) Lookup(int v)
        {
            if (_table == null) return (Default, 0);
            int idx = v >> (16 - _w); int r = idx < _table.Length ? _table[idx] : 0;
            return (r, Lengths![r]);
        }
    }
    byte[] _in = Array.Empty<byte>(); long _pos; int _blk; Huffman? _ch, _di;
    int Bits(long start, int len)
    {
        long end = start + len - 1; long sb = start / 8, eb = end / 8; long v = 0;
        for (long i = sb; i <= eb; i++) { v <<= 8; if (i < _in.Length) v |= _in[i]; }
        int unused = (int)((8 - (end + 1) % 8) % 8);
        return (int)((v >> unused) & ((1L << len) - 1));
    }
    int Peek(int n) => Bits(_pos, n);
    int Pop(int n) { var v = Peek(n); _pos += n; return v; }
    int VarLen() { int m = Pop(3); if (m != 7) return m; for (int q = 0; q < 13; q++) { if (Pop(1) == 1) m++; else break; } return m; }
    Huffman LoadCharLen()
    {
        int count = Pop(5); if (count == 0) return new Huffman(null, Pop(5));
        var l = new int[count]; int i = 0;
        while (i < count) { if (i == 3) i += Pop(2); if (i >= count) break; l[i] = VarLen(); i++; }
        var h = new Huffman(l, 8); h.Build(); return h;
    }
    Huffman LoadChar(Huffman lh)
    {
        int count = Pop(9); if (count == 0) return new Huffman(null, Pop(9));
        var l = new int[count]; int i = 0;
        while (i < count)
        {
            var (c, len) = lh.Lookup(Peek(16)); _pos += len;
            if (c == 0) i += 1; else if (c == 1) i += 3 + Pop(4); else if (c == 2) i += 20 + Pop(9);
            else { l[i] = c - 2; i++; }
        }
        var h = new Huffman(l); h.Build(); return h;
    }
    Huffman LoadDist()
    {
        int count = Pop(5); if (count == 0) return new Huffman(null, Pop(5));
        var l = new int[count]; for (int i = 0; i < count; i++) l[i] = VarLen();
        var h = new Huffman(l); h.Build(); return h;
    }
    public static List<byte> Expand(byte[] data, int? size = null) => new EmbCompress().Decompress(data, size);
    List<byte> Decompress(byte[] data, int? size)
    {
        _in = data; var o = new List<byte>(size ?? 1024); _blk = -1; long total = data.Length * 8L;
        while (total > _pos && (size == null || o.Count <= size))
        {
            if (_blk <= 0) { _blk = Pop(16); var cl = LoadCharLen(); _ch = LoadChar(cl); _di = LoadDist(); }
            _blk--;
            var (ch, l) = _ch!.Lookup(Peek(16)); _pos += l;
            if (ch <= 255) o.Add((byte)ch);
            else if (ch == 510) break;
            else
            {
                int length = ch - 253;
                var (dv, dl) = _di!.Lookup(Peek(16)); _pos += dl;
                int back = (dv == 0 ? 0 : (1 << (dv - 1)) + Pop(dv - 1)) + 1;
                int pos = o.Count - back; if (pos < 0) break;
                for (int i = 0; i < length; i++) o.Add(o[pos + i]);
            }
        }
        return o;
    }
}

/// <summary>Pfaff/Husqvarna VP3 (pyembroidery Vp3Reader), big-endian colour blocks.</summary>
public sealed class Vp3Parser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".vp3" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        if (!(d.Length >= 5 && Encoding.ASCII.GetString(d, 0, 5) == "%vsm%")) throw new InvalidDataException("Not a VP3 file");
        var p = ParserUtil.New(fileName);
        int o = 6;
        void Skip() { o += 2 + Rd.U16BE(d, o); }
        Skip(); o += 7; Skip(); o += 32;
        double cx = Rd.I32BE(d, o) / 100.0, cy = -(Rd.I32BE(d, o + 4) / 100.0); o += 8;
        o += 27; Skip(); o += 24; Skip();
        int ncol = Rd.U16BE(d, o); o += 2;
        var b = new StitchBuilder(p);
        double ax = 0, ay = 0;
        for (int ci = 0; ci < ncol && o + 7 < d.Length; ci++)
        {
            o += 3;
            int dist = Rd.I32BE(d, o); o += 4;
            int end = dist + o;
            double sx = Rd.I32BE(d, o) / 100.0, sy = -(Rd.I32BE(d, o + 4) / 100.0); o += 8;
            double tx = sx + cx, ty = sy + cy;
            if (tx != 0 && ty != 0) { b.Add(tx - ax, ty - ay, EmbroideryCommand.Jump); ax = tx; ay = ty; }
            int colors = d[o]; o += 2; uint rgb = 0;
            for (int m = 0; m < colors; m++) { rgb = (uint)(d[o] << 16 | d[o + 1] << 8 | d[o + 2]); o += 6; }
            o += 2; // type, weight
            Skip(); Skip(); Skip();
            p.Colors.Add(Palettes.Rgb(rgb));
            o += 15 + 3;
            if (end > d.Length || end < o) end = d.Length;
            int i = o;
            while (i < end - 1)
            {
                int x = (sbyte)d[i], y = (sbyte)d[i + 1]; i += 2;
                if ((x & 0xFF) != 0x80) { b.Add(x, y, EmbroideryCommand.Stitch); ax += x; ay += y; continue; }
                if (y == 0x01 && i + 5 < end)
                {
                    int bx = (short)(d[i] << 8 | d[i + 1]), by = (short)(d[i + 2] << 8 | d[i + 3]); i += 6;
                    b.Add(bx, by, EmbroideryCommand.Stitch); ax += bx; ay += by;
                }
                else if (y == 0x03) b.Add(0, 0, EmbroideryCommand.Trim);
            }
            o = end;
            if (ci + 1 < ncol) b.ColorChange();
        }
        b.End();
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Singer XXX (pyembroidery XxxReader) incl. 0x7D/0x7E long moves and trailing RGB palette.</summary>
public sealed class XxxParser : IEmbroideryParser
{
    public string[] Extensions => new[] { ".xxx" };
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        var d = ParserUtil.ReadAll(stream);
        if (d.Length < 0x100) throw new InvalidDataException("XXX too short");
        var p = ParserUtil.New(fileName);
        int ncol = Rd.U16LE(d, 0x27);
        var b = new StitchBuilder(p);
        int o = 0x100;
        while (o < d.Length)
        {
            byte b1 = d[o++];
            if (b1 == 0x7D || b1 == 0x7E)
            {
                if (o + 3 >= d.Length) break;
                b.Add((short)Rd.U16LE(d, o), -(short)Rd.U16LE(d, o + 2), EmbroideryCommand.Jump); o += 4; continue;
            }
            if (o >= d.Length) break;
            byte b2 = d[o++];
            if (b1 != 0x7F) { b.Add(Rd.S8(b1), -Rd.S8(b2), EmbroideryCommand.Stitch); continue; }
            if (o + 1 >= d.Length) break;
            int x = Rd.S8(d[o]), y = -Rd.S8(d[o + 1]); o += 2;
            if (b2 == 0x01) b.Add(x, y, EmbroideryCommand.Jump);
            else if (b2 == 0x03) { b.Add(0, 0, EmbroideryCommand.Trim); if (x != 0 || y != 0) b.Add(x, y, EmbroideryCommand.Jump); }
            else if (b2 == 0x08 || (b2 >= 0x0A && b2 <= 0x17)) b.ColorChange();
            else if (b2 == 0x7F || b2 == 0x18) break;
        }
        b.End();
        o += 2;
        for (int i = 0; i < ncol && o + 3 < d.Length; i++, o += 4) p.Colors.Add(Palettes.Rgb((uint)(d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3])));
        Palettes.FillMissing(p);
        p.ComputeStats();
        return p;
    }
}

/// <summary>Header-only parser for formats without stitch decoding (VIP, VP4, SHV, PCM, PCQ, PCD, KSM, CSD, EMB, DES).</summary>
public sealed class HeaderOnlyParser : IEmbroideryParser
{
    readonly string[] _exts;
    public HeaderOnlyParser(params string[] exts) => _exts = exts;
    public string[] Extensions => _exts;
    public EmbroideryPattern Parse(Stream stream, string fileName)
    {
        ParserUtil.ReadAll(stream);
        var p = ParserUtil.New(fileName);
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
        new PesParser(), new DstParser(), new DstParser(DstParser.Flavor.Dsb), new DstParser(DstParser.Flavor.Dsz), new DstParser(DstParser.Flavor.Tap),
        new U01Parser(), new ExpParser(), new JefParser(), new XxxParser(), new SewParser(), new PcsParser(), new HusParser(), new Vp3Parser(),
        new HeaderOnlyParser(".vp4", ".vip", ".shv", ".pcm", ".pcq", ".pcd", ".ksm", ".csd", ".emb", ".des"),
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
