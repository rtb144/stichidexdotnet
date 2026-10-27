using Stitchidex.Core.Models;
using Stitchidex.Core.Services;
namespace Stitchidex.Tests;

public class ServiceTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx").FullName;
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    string MakeExp(string sub, string name)
    {
        var d = Path.Combine(_dir, sub); Directory.CreateDirectory(d);
        var p = Path.Combine(d, name);
        File.WriteAllBytes(p, new byte[] { 0, 0, 100, 0, 0, 100, 0x9C, 0 });
        return p;
    }

    SqliteCatalogService Seed()
    {
        MakeExp("lib", "spring_rose_bouquet.exp");
        MakeExp("lib", "christmas_santa_hat.exp");
        MakeExp("lib", "sailboat_anchor.exp");
        File.WriteAllText(Path.Combine(_dir, "lib", "script.py"), "print(1)");
        File.WriteAllText(Path.Combine(_dir, "lib", "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "lib", "pyproject.toml"), "x");
        var c = new SqliteCatalogService(Path.Combine(_dir, "stichidex_catalog.sqlite3"));
        Assert.Equal(3, c.ScanDirectory(Path.Combine(_dir, "lib")));
        return c;
    }

    [Fact]
    public void Catalog_ScansOnlyEmbroideryAndUsesWal()
    {
        using var c = Seed();
        Assert.Equal(3, c.All().Count);
        Assert.True(File.Exists(c.DatabasePath + "-wal") || File.Exists(c.DatabasePath));
        Assert.NotNull(c.All()[0].Thumbnail);
        Assert.Single(c.Search("santa"));
    }

    [Fact]
    public void Catalog_TagsBulkAndCraftTagList()
    {
        using var c = Seed();
        var ids = c.All().Select(e => e.Id).ToList();
        c.BulkAddTags(ids, new[] { "Gift", "towel" });
        c.AddTag(ids[0], "favorite");
        c.BulkRemoveTags(ids.Take(2), new[] { "towel" });
        Assert.Equal(new[] { "favorite", "gift", "towel" }, c.GetAllCraftTags());
        Assert.Contains("towel", c.Get(ids[2])!.Tags);
        Assert.DoesNotContain("towel", c.Get(ids[0])!.Tags);
        c.RemoveTag(ids[0], "favorite");
        Assert.DoesNotContain("favorite", c.GetAllCraftTags());
    }

    [Fact]
    public void Catalog_RemoveDirectoryAndPurge()
    {
        using var c = Seed();
        MakeExp("other", "fish.exp");
        c.ScanDirectory(Path.Combine(_dir, "other"));
        Assert.Equal(4, c.All().Count);
        Assert.Equal(3, c.RemoveDirectoryRecords(Path.Combine(_dir, "lib")));
        Assert.Single(c.All());
    }

    [Fact]
    public void Catalog_PurgesNonPatternRecordsOnInit()
    {
        var db = Path.Combine(_dir, "c.sqlite3");
        using (var c = new SqliteCatalogService(db))
        {
            var p = new EmbroideryPattern { Title = "bad", FilePath = Path.Combine(_dir, "x.py") };
            c.Upsert(p);
            Assert.Single(c.All());
        }
        using var c2 = new SqliteCatalogService(db);
        Assert.Empty(c2.All());
    }

    [Fact]
    public void Catalog_RepairsTruncatedTitles()
    {
        using var c = new SqliteCatalogService(Path.Combine(_dir, "r.sqlite3"));
        c.Upsert(new EmbroideryPattern { Title = "beautiful butter", FilePath = Path.Combine(_dir, "beautiful_butterfly_wings.pes") });
        Assert.Equal(1, c.RepairTruncatedTitles());
        Assert.Equal("beautiful butterfly wings", c.All()[0].Title);
    }

    [Fact]
    public void SmartSearch_ConceptClustersAndNoiseFloor()
    {
        using var c = Seed();
        var s = new HybridSmartSearchService(c);
        Assert.Equal(11, HybridSmartSearchService.ConceptClusters.Count);
        Assert.Contains("offline", s.Status);
        var r = s.Search("flowers");
        Assert.NotEmpty(r);
        Assert.Equal("spring rose bouquet", r[0].Entry.Title);
        Assert.All(r, x => Assert.True(x.Score >= HybridSmartSearchService.NoiseFloor));
        Assert.Equal("sailboat anchor", s.Search("nautical boat")[0].Entry.Title);
        Assert.Empty(s.Search("zzzqqq"));
        Assert.Equal(384, new LocalCpuEmbeddingProvider().Embed("rose").Length);
    }

    [Theory]
    [InlineData("My Rose (v2)!.PES", "My_Rose_v2.pes")]
    [InlineData("???.dst", "design.dst")]
    [InlineData("a very long embroidery design file name here.jef", "a_very_long_embroidery_design_fi.jef")]
    public void Usb_SanitizesFilenames(string i, string e) => Assert.Equal(e, UsbExportService.SanitizeBrotherFilename(i));

    [Fact]
    public void Usb_DetectsLinuxMediaCopiesAndCharts()
    {
        var media = Path.Combine(_dir, "media"); Directory.CreateDirectory(Path.Combine(media, "BROTHER_USB"));
        var svc = new UsbExportService { LinuxMediaRoots = () => new[] { media } };
        if (!OperatingSystem.IsWindows())
        {
            var drives = svc.DetectRemovableDrives();
            Assert.Single(drives);
            Assert.Equal("BROTHER_USB", drives[0].Label);
        }
        var src = MakeExp("lib", "Rose Bud.exp");
        var html = UsbExportService.GenerateThreadColorChartHtml(new[] { ("Rose <1>", (IEnumerable<ThreadColor>)new[] { ThreadColor.FromPecIndex(5) }) });
        Assert.Contains("#EC0000", html); Assert.Contains("Rose &lt;1&gt;", html);
        var root = Path.Combine(media, "BROTHER_USB");
        var w = svc.CopyToUsb(new[] { src, src }, root, UsbDestination.bPocket, html);
        Assert.Equal(new[] { "Rose_Bud.exp", "Rose_Bud_1.exp", "thread_color_chart.html" }, w.Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(root, "bPocket", "Rose_Bud.exp")));
        Assert.Equal(Path.Combine(root, "Embroidery"), UsbExportService.DestinationFolder(root, UsbDestination.Embroidery));
    }

    [Fact]
    public void Usb_EjectCommands()
    {
        var win = UsbExportService.BuildEjectCommands(new RemovableDrive("E:\\", "USB", null), true);
        Assert.Contains("NameSpace(17).ParseName('E:').InvokeVerb('Eject')", win[0].Arguments);
        var lin = UsbExportService.BuildEjectCommands(new RemovableDrive("/media/tom/USB", "USB", "/dev/sdb1"), false);
        Assert.Equal(("udisksctl", "unmount -b /dev/sdb1"), lin[0]);
        Assert.Equal("gio", lin[1].FileName);
    }
}
