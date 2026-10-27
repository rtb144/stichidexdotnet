using Stitchidex.Core.Parsers;
using Stitchidex.Core.Services;
namespace Stitchidex.Tests;

[Collection("UsbVolumes")]
public class RemovableDriveHandlingTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stxusb").FullName;
    string UsbD => Path.Combine(_dir, "mnt_D");
    public RemovableDriveHandlingTests()
    {
        Directory.CreateDirectory(UsbD);
        UsbDriveService.OfflineCacheDir = Path.Combine(_dir, "offline_cache");
    }
    public void Dispose()
    {
        UsbDriveService.VolumeProvider = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    static string Sample() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (!Directory.Exists(Path.Combine(d.FullName, "samples"))) d = d.Parent!; return Directory.GetFiles(Path.Combine(d.FullName, "samples")).First(f => f.EndsWith(".pes", StringComparison.OrdinalIgnoreCase)); }

    string PutOnUsb(string root, string rel = "Embroidery/Rose.pes")
    {
        var f = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.Copy(Sample(), f, true); return f;
    }

    [Fact]
    public void InspectAndCatalog_StoresVolumeInfoAndBlob()
    {
        var f = PutOnUsb(UsbD);
        UsbDriveService.VolumeProvider = () => new[] { (UsbD, "GRANDMA", "1A2B3C4D") };
        var v = UsbDriveService.InspectPathVolume(f);
        Assert.True(v.IsRemovable);
        Assert.Equal("GRANDMA", v.VolumeLabel);
        Assert.Equal(Path.Combine("Embroidery", "Rose.pes"), v.VolumeRelativePath);
        using var c = new SqliteCatalogService(Path.Combine(_dir, "c.db"));
        Assert.Equal(1, c.ScanDirectory(UsbD));
        var e = c.All().Single();
        Assert.True(e.IsFromRemovableDrive); Assert.True(e.HasCachedBlob);
        Assert.Equal("1A2B3C4D", e.VolumeSerial);
        Assert.Equal(File.ReadAllBytes(f), c.GetCachedPatternBytes(e.Id));
    }

    [Fact]
    public void DriveLetterRemap_FindsFileOnNewRoot()
    {
        PutOnUsb(UsbD);
        UsbDriveService.VolumeProvider = () => new[] { (UsbD, "GRANDMA", "1A2B3C4D") };
        using var c = new SqliteCatalogService(Path.Combine(_dir, "c.db"));
        c.ScanDirectory(UsbD);
        var e = c.All().Single();
        var usbE = Path.Combine(_dir, "mnt_E");
        Directory.Move(UsbD, usbE); // same stick, new drive letter
        var r = UsbDriveService.ResolvePatternSourcePath(e, c, new[] { (usbE, "GRANDMA", "1A2B3C4D") });
        Assert.Equal(Path.Combine(usbE, "Embroidery", "Rose.pes"), r.Path);
        Assert.False(r.FromOfflineCache);
        Assert.Contains("Connected", r.Status);
        // label match when serial unknown
        var e2 = e with { VolumeSerial = "" };
        Assert.NotNull(UsbDriveService.ResolvePatternSourcePath(e2, null, new[] { (usbE, "grandma", "") }).Path);
    }

    [Fact]
    public void UnpluggedUsb_ExportsFromOfflineCache()
    {
        var original = File.ReadAllBytes(PutOnUsb(UsbD));
        UsbDriveService.VolumeProvider = () => new[] { (UsbD, "GRANDMA", "1A2B3C4D") };
        using var c = new SqliteCatalogService(Path.Combine(_dir, "c.db"));
        c.ScanDirectory(UsbD);
        var e = c.All().Single();
        Directory.Delete(UsbD, true); // unplugged
        UsbDriveService.VolumeProvider = () => Array.Empty<(string, string, string)>();
        var machineUsb = Path.Combine(_dir, "machine"); Directory.CreateDirectory(machineUsb);
        var missing = new List<string>();
        var w = new UsbExportService().ExportPatterns(new[] { e }, c, machineUsb, UsbDestination.Root, null, missing);
        Assert.Empty(missing);
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(w)));
        Assert.Contains("Offline Cache", UsbDriveService.ResolvePatternSourcePath(e, c).Status);
    }

    [Fact]
    public void UnpluggedWithoutCache_ReportsInsertStick()
    {
        var e = new CatalogEntry(1, Path.Combine(_dir, "gone.pes"), "x", "", 0, 0, 0, 0, "", "", Array.Empty<string>(), "", null) { VolumeLabel = "OLDSTICK", VolumeRelativePath = "gone.pes", IsFromRemovableDrive = true };
        var r = UsbDriveService.ResolvePatternSourcePath(e, null, Array.Empty<(string, string, string)>());
        Assert.Null(r.Path);
        Assert.Contains("Insert 'OLDSTICK'", r.Status);
    }

    [Fact]
    public void CopyToComputer_CopiesPatternsAndSidecars()
    {
        var f = PutOnUsb(UsbD, "Winter/Snowman.pes");
        File.WriteAllText(Path.ChangeExtension(f, ".edr"), "x");
        File.WriteAllText(Path.Combine(UsbD, "readme.txt"), "skip");
        var lib = Path.Combine(_dir, "Stitchidex Library");
        var dest = UsbDriveService.CopyFolderPatternsToLocalLibrary(UsbD, lib);
        Assert.Equal(Path.Combine(lib, "mnt_D"), dest);
        Assert.True(File.Exists(Path.Combine(dest, "Winter", "Snowman.pes")));
        Assert.True(File.Exists(Path.Combine(dest, "Winter", "Snowman.edr")));
        Assert.False(File.Exists(Path.Combine(dest, "readme.txt")));
        using var c = new SqliteCatalogService(Path.Combine(_dir, "c.db"));
        Assert.Equal(1, c.ScanDirectory(dest));
        Assert.False(c.All().Single().IsFromRemovableDrive);
    }

    [Fact]
    public void ParserDoesNotLockFiles()
    {
        var f = PutOnUsb(UsbD);
        using var writer = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.True(EmbroideryParserRegistry.ParseFile(f).StitchCount > 0);
    }
}
