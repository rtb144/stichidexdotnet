using System.Text;
using Stitchidex.Core.Models;
using Stitchidex.Core.Parsers;
using Stitchidex.Core.Rendering;
namespace Stitchidex.Tests;

public class ParserTests
{
    static byte[] BuildPes(string label, byte[] colors, byte[] stitchBytes)
    {
        var ms = new MemoryStream();
        var hdr = new byte[16]; Encoding.ASCII.GetBytes("#PES0001").CopyTo(hdr, 0);
        BitConverter.GetBytes(16).CopyTo(hdr, 8);
        ms.Write(hdr);
        var pec = new byte[532];
        Array.Fill(pec, (byte)0x20, 3, 16);
        Encoding.ASCII.GetBytes("LA:").CopyTo(pec, 0);
        Encoding.ASCII.GetBytes(label.PadRight(16)[..16]).CopyTo(pec, 3);
        pec[48] = (byte)(colors.Length - 1);
        colors.CopyTo(pec, 49);
        ms.Write(pec); ms.Write(stitchBytes);
        return ms.ToArray();
    }

    [Fact]
    public void Pes_DecodesShortLongJumpTrimAndColorChange()
    {
        var s = new List<byte>();
        s.AddRange(new byte[] { 10, 0 });            // stitch +10,0
        s.AddRange(new byte[] { 0, 10 });            // stitch 0,+10
        s.AddRange(new byte[] { 0x7F - 0x0A + 1 + 0x00, 0 }); // 0x76 => -10
        s.AddRange(new byte[] { 0x90, 0xC8, 0x80, 0x00 }); // jump x=+200 (long), y=0 (long)
        s.AddRange(new byte[] { 0xA0, 0x05, 0x00 }); // trim long x=+5, y short 0
        s.AddRange(new byte[] { 0xFE, 0xB0, 0x02 }); // color change
        s.AddRange(new byte[] { 0x8F, 0x38, 5 });    // long x = 0xF38 => -200 (jump flag absent => stitch), y=5
        s.Add(0xFF);
        var data = BuildPes("BUTTERFL", new byte[] { 5, 13 }, s.ToArray());
        var p = new PesParser().Parse(new MemoryStream(data), "spring_butterfly_garden.pes");
        Assert.Equal("spring butterfly garden", p.Title);
        Assert.Contains("pec:BUTTERFL", p.Tags);
        Assert.Equal(2, p.Colors.Count);
        Assert.Equal("Red", p.Colors[0].Name);
        Assert.Equal("Yellow", p.Colors[1].Name);
        Assert.Equal(1, p.JumpCount);
        Assert.Equal(1, p.TrimCount);
        Assert.Equal(1, p.ColorChangeCount);
        Assert.Equal(4, p.StitchCount);
        var last = p.Stitches.Last(x => x.Command == EmbroideryCommand.Stitch);
        Assert.Equal(0 + 200 + 5 - 200, last.X);
        Assert.Equal(15, last.Y);
        Assert.Equal(1, last.ColorIndex);
    }

    [Fact]
    public void Pes_FallsBackToPecLabelForGenericFilename()
    {
        var data = BuildPes("Rose Bouquet", new byte[] { 1 }, new byte[] { 1, 1, 0xFF });
        var p = new PesParser().Parse(new MemoryStream(data), "123.pes");
        Assert.Equal("Rose Bouquet", p.Title);
    }

    [Fact]
    public void Pes_RejectsBadMagic() =>
        Assert.Throws<InvalidDataException>(() => new PesParser().Parse(new MemoryStream(new byte[600]), "x.pes"));

    static byte[] DstRecord(int dx, int dy, byte flags)
    {
        int b0 = 0, b1 = 0, b2 = flags | 0x03;
        dy = -dy;
        // Greedy balanced ternary for |v| <= 121
        (int w, int bx, int px, int nx, int py, int ny)[] t = { (81,2,2,3,5,4), (27,1,2,3,5,4), (9,0,2,3,5,4), (3,1,0,1,7,6), (1,0,0,1,7,6) };
        int[] bytes = { b0, b1, b2 };
        int x = dx, y = dy;
        foreach (var (w, bi, px, nx, py, ny) in t)
        {
            if (x > w / 2) { bytes[bi] |= 1 << px; x -= w; } else if (x < -w / 2) { bytes[bi] |= 1 << nx; x += w; }
            if (y > w / 2) { bytes[bi] |= 1 << py; y -= w; } else if (y < -w / 2) { bytes[bi] |= 1 << ny; y += w; }
        }
        return new[] { (byte)bytes[0], (byte)bytes[1], (byte)bytes[2] };
    }

    [Fact]
    public void Dst_DecodesBalancedTernary()
    {
        var ms = new MemoryStream();
        var hdr = Encoding.ASCII.GetBytes("LA:TESTDESIGN      \r".PadRight(512, ' '));
        ms.Write(hdr);
        ms.Write(DstRecord(50, 0, 0)); ms.Write(DstRecord(0, -121, 0)); ms.Write(DstRecord(-7, 13, 0x80));
        ms.Write(DstRecord(0, 0, 0xC0)); ms.Write(DstRecord(1, 1, 0));
        ms.Write(new byte[] { 0, 0, 0xF3 });
        var p = new DstParser().Parse(new MemoryStream(ms.ToArray()), "dst_test.dst");
        Assert.Contains("dst:TESTDESIGN", p.Tags);
        var st = p.Stitches;
        Assert.Equal((50.0, 0.0), (st[0].X, st[0].Y));
        Assert.Equal((50.0, -121.0), (st[1].X, st[1].Y));
        Assert.Equal(EmbroideryCommand.Jump, st[2].Command);
        Assert.Equal((43.0, -108.0), (st[2].X, st[2].Y));
        Assert.Equal(1, p.ColorChangeCount);
        Assert.Equal(3, p.StitchCount);
        Assert.Equal(2, p.Colors.Count);
    }

    [Fact]
    public void Exp_DecodesEscapes()
    {
        var d = new byte[] { 10, 0, 0x80, 0x04, 20, 0, 0x80, 0x01, 0, 0, 0xF6, 0xF6, 0x80, 0x80, 5, 5 };
        var p = new ExpParser().Parse(new MemoryStream(d), "a.exp");
        Assert.Equal(2, p.StitchCount);
        Assert.Equal(1, p.JumpCount); // pyembroidery: colour change with (0,0) adds no move
        Assert.Equal(1, p.TrimCount);
        Assert.Equal(1, p.ColorChangeCount);
        var s = p.Stitches.First(x => x.Command == EmbroideryCommand.Trim); // pyembroidery: trim ignores its dx/dy
        Assert.Equal((20.0, 10.0), (s.X, s.Y));
    }

    [Fact]
    public void Jef_ReadsHeaderPaletteAndStitches()
    {
        var hdr = new byte[0x74 + 8];
        BitConverter.GetBytes(hdr.Length).CopyTo(hdr, 0);
        BitConverter.GetBytes(2).CopyTo(hdr, 0x18);
        BitConverter.GetBytes(3).CopyTo(hdr, 0x1C);
        BitConverter.GetBytes(26).CopyTo(hdr, 0x74);
        BitConverter.GetBytes(6).CopyTo(hdr, 0x78);
        var st = new byte[] { 0, 0, 100, 0, 0x80, 0x01, 0, 0, 0, 100, 0x80, 0x02, 10, 10, 0x80, 0x10 };
        var p = new JefParser().Parse(new MemoryStream(hdr.Concat(st).ToArray()), "j.jef");
        Assert.Equal(new[] { "Baby Blue", "Green" }, p.Colors.Select(c => c.Name)); // full 80-entry pyembroidery Janome palette
        Assert.Equal(3, p.StitchCount);
        Assert.Equal(1, p.ColorChangeCount);
        Assert.Equal(10.0, p.WidthMm);
        Assert.Equal(10.0, p.HeightMm);
        Assert.Equal("100x100mm (4x4 in)", p.HoopCategory);
    }

    [Fact]
    public void Palette_HasSixtyFourColorsAndLookups()
    {
        Assert.Equal(65, ThreadColor.BrotherPecPalette.Count);
        Assert.Equal("#EC0000", ThreadColor.FromPecIndex(5).Hex);
        Assert.Equal("Black", ThreadColor.FindByName("black")!.Name);
        Assert.Equal(0x1a, ThreadColor.FromHex("#1A0A94").R);
    }

    [Theory]
    [InlineData(".pes", true)] [InlineData(".DST", true)] [InlineData(".vp3", true)] [InlineData(".py", false)] [InlineData(".txt", false)]
    public void Registry_FormatMap(string ext, bool ok) => Assert.Equal(ok, EmbroideryParserRegistry.IsEmbroideryExtension(ext));

    [Theory]
    [InlineData(90, 90, "100x100mm (4x4 in)")] [InlineData(170, 120, "130x180mm (5x7 in)")] [InlineData(250, 150, "160x260mm (6x10 in)")] [InlineData(500, 500, "Oversize (> 240x360mm)")]
    public void Hoop_Classification(double w, double h, string e) => Assert.Equal(e, EmbroideryPattern.ClassifyHoop(w, h));

    [Fact]
    public void Thumbnail_RendersPng()
    {
        var p = new ExpParser().Parse(new MemoryStream(new byte[] { 50, 0, 0, 50, 0xCE, 0, 0, 0xCE }), "sq.exp");
        var png = ThumbnailRenderer.RenderThumbnailPng(p);
        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        using var bmp = SkiaSharp.SKBitmap.Decode(png);
        Assert.Equal(256, bmp.Width);
        Assert.Equal(0, bmp.GetPixel(2, 2).Alpha);
    }

    [Fact]
    public void RepoSampleFiles_ParseWithoutThrowing()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var files = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/dotnet/") && new[] { ".pes", ".dst", ".exp", ".jef" }.Contains(Path.GetExtension(f).ToLowerInvariant())).Take(50).ToList() : new();
        foreach (var f in files) Assert.NotNull(EmbroideryParserRegistry.ParseFile(f));
    }
}
