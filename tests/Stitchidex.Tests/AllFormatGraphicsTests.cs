using SkiaSharp;
using Stitchidex.Core.Parsers;
using Stitchidex.Core.Rendering;
using Stitchidex.Core.Services;
using Xunit;

namespace Stitchidex.Tests;

public class AllFormatGraphicsTests
{
    static string SamplesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "samples"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples");
    }

    static int VisiblePixels(byte[] png)
    {
        using var bm = SKBitmap.Decode(png);
        Assert.Equal(256, bm.Width); Assert.Equal(256, bm.Height);
        int n = 0;
        for (int y = 0; y < bm.Height; y++) for (int x = 0; x < bm.Width; x++) if (bm.GetPixel(x, y).Alpha > 0) n++;
        return n;
    }

    [Theory]
    [InlineData("scene-wilcom-v4.pes")] [InlineData("scene-wilcom.PEC")] [InlineData("random2-ew.dst")]
    [InlineData("scene-wilcom.EXP")] [InlineData("scene-me.jef")] [InlineData("random1-wilcom.DSB")]
    [InlineData("scene-wilcom.DSZ")] [InlineData("scene-wilcom.HUS")] [InlineData("scene-premier.vp3")]
    [InlineData("scene-wilcom.SEW")] [InlineData("scene-wilcom.PCS")] [InlineData("scene-wilcom-s.XXX")]
    [InlineData("scene-wilcom.U01")] [InlineData("scene-me.tap")] [InlineData("scene-ew.DST.dst")]
    public void EveryFormatParsesStitchesAndRendersVisibleThumbnail(string name)
    {
        var p = EmbroideryParserRegistry.ParseFile(Path.Combine(SamplesDir(), name));
        Assert.True(p.StitchCount > 0, $"{name}: no stitches");
        Assert.True(p.Colors.Count > 0, $"{name}: no colors");
        Assert.True(p.WidthMm > 0 && p.HeightMm > 0, $"{name}: zero size");
        var png = ThumbnailRenderer.RenderThumbnailPng(p);
        Assert.True(VisiblePixels(png) > 500, $"{name}: thumbnail nearly empty");
    }

    [Fact]
    public void DstSidecarEdrColorsAreUsed()
    {
        var p = EmbroideryParserRegistry.ParseFile(Path.Combine(SamplesDir(), "scene-ew.DST.dst"));
        Assert.DoesNotContain(p.Colors, c => c.Name.StartsWith("Studio"));
    }

    [Fact]
    public void PalettelessFormatsGetVibrantStudioColors()
    {
        var p = EmbroideryParserRegistry.ParseFile(Path.Combine(SamplesDir(), "random2-ew.dst"));
        Assert.All(p.Colors, c => Assert.True(0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 60, $"{c.Hex} too dark"));
    }

    [Fact]
    public void VipIsHeaderOnlyWithoutCrashing()
    {
        var p = EmbroideryParserRegistry.ParseFile(Path.Combine(SamplesDir(), "scene-me.vip"));
        Assert.Contains("header-only", p.Tags);
    }

    [Fact]
    public async Task ScanDirectoryAsyncRunsInBackgroundAndReportsProgress()
    {
        var db = Path.Combine(Path.GetTempPath(), $"stx_{Guid.NewGuid():N}.sqlite3");
        try
        {
            using var svc = new SqliteCatalogService(db);
            var reports = new List<(int Done, int Total, string File)>();
            var progress = new SyncProgress(r => { lock (reports) reports.Add(r); });
            int caller = Environment.CurrentManagedThreadId;
            var task = svc.ScanDirectoryAsync(SamplesDir(), progress);
            int n = await task;
            Assert.True(n >= 15);
            Assert.Contains(reports, r => r.Done == r.Total && r.Total >= 15);
            var all = svc.All();
            Assert.All(all.Where(e => e.StitchCount > 0), e => Assert.True(e.Thumbnail is { Length: > 0 }));
            Assert.Contains(all, e => e.PaletteHex.Count > 0);
            Assert.Equal(0, await svc.RepairMissingThumbnailsAsync());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(db) + "*")) File.Delete(f); }
    }

    sealed class SyncProgress : IProgress<(int Done, int Total, string FileName)>
    {
        readonly Action<(int, int, string)> _a; public SyncProgress(Action<(int, int, string)> a) => _a = a;
        public void Report((int Done, int Total, string FileName) v) => _a(v);
    }
}
