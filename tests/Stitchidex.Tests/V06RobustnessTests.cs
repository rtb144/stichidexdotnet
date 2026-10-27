using System.Runtime.InteropServices;
using Stitchidex.Core.Parsers;
using Stitchidex.Core.Search;
using Stitchidex.Core.Services;
namespace Stitchidex.Tests;

[Collection("UsbVolumes")]
public class V06RobustnessTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stxv06").FullName;
    public void Dispose()
    {
        UsbDriveService.VolumeProvider = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    static string Root() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (!Directory.Exists(Path.Combine(d.FullName, "samples"))) d = d.Parent!; return d.FullName; }
    static string SamplePes() => Directory.GetFiles(Path.Combine(Root(), "samples")).First(f => f.EndsWith(".pes", StringComparison.OrdinalIgnoreCase));

    SqliteCatalogService Catalog(out long id)
    {
        var lib = Path.Combine(_dir, "lib"); Directory.CreateDirectory(lib);
        File.Copy(SamplePes(), Path.Combine(lib, "rose_garden.pes"), true);
        var c = new SqliteCatalogService(Path.Combine(_dir, "c.db"));
        Assert.Equal(1, c.ScanDirectory(lib));
        id = c.All().Single().Id;
        return c;
    }

    [Fact]
    public void AllMetadataRaw_OmitsThumbnailBlob_AndTagOpsStillWork()
    {
        using var c = Catalog(out var id);
        Assert.NotNull(c.All().Single().Thumbnail);
        var meta = c.AllMetadataRaw().Single();
        Assert.Null(meta.Thumbnail);
        Assert.Equal(id, meta.Id);
        c.BulkAddTags(new[] { id }, new[] { "Christmas", "quilt" });
        Assert.Contains("christmas", c.GetAllCraftTags());
        Assert.Contains("quilt", c.AllMetadataRaw().Single().Tags);
        Assert.Equal(0, c.PurgeNonPatternRecords());
    }

    [Fact]
    public void TargetedFtsUpdates_MakeNotesAndTagsSearchableImmediately()
    {
        using var c = Catalog(out var id);
        Assert.Empty(c.FullTextSearch("grandkids"));
        c.SetNotes(id, "Stitched for the grandkids birthday");
        Assert.Contains(c.FullTextSearch("grandkids"), h => h.Id == id);
        c.SetNotes(id, "Something else");
        Assert.Empty(c.FullTextSearch("grandkids"));
        c.BulkAddTags(new[] { id }, new[] { "zebrastripe" });
        Assert.Contains(c.FullTextSearch("zebrastripe"), h => h.Id == id);
        c.BulkRemoveTags(new[] { id }, new[] { "zebrastripe" });
        Assert.Empty(c.FullTextSearch("zebrastripe"));
        Assert.Contains(c.FullTextSearch("rose"), h => h.Id == id);
    }

    [Fact]
    public void ScanDirectory_SkipsCorruptFile_WithoutAbortingChunk()
    {
        var lib = Path.Combine(_dir, "lib"); Directory.CreateDirectory(lib);
        File.Copy(SamplePes(), Path.Combine(lib, "good.pes"));
        var bad = new byte[64]; "#PES0001"u8.ToArray().CopyTo(bad, 0); BitConverter.GetBytes(int.MaxValue).CopyTo(bad, 8);
        File.WriteAllBytes(Path.Combine(lib, "corrupt.pes"), bad);
        File.WriteAllBytes(Path.Combine(lib, "garbage.dst"), Enumerable.Repeat((byte)0xFF, 10).ToArray());
        using var c = new SqliteCatalogService(Path.Combine(_dir, "c.db"));
        var n = c.ScanDirectory(lib);
        Assert.Contains(c.All(), e => e.Path.EndsWith("good.pes"));
        Assert.True(n >= 1);
    }

    [Fact]
    public void ReadAll_RejectsOversizedInput()
    {
        using var s = new MemoryStream(new byte[ParserUtil.MaxFileBytes + 1]);
        Assert.Throws<InvalidDataException>(() => ParserUtil.ReadAll(s));
    }

    [Theory]
    [InlineData("crème brûlée", "brûlée")]
    [InlineData("Größe blümchen", "blümchen")]
    public void SpellChecker_PreservesUnicodeWords(string query, string word)
    {
        var vocab = SearchSpellChecker.BuildVocabulary(new[] { "crème", "brûlée", "größe", "blümchen" });
        var s = SearchSpellChecker.SuggestCorrections(query, vocab);
        Assert.Null(s.CorrectedQuery); // known accented words are tokenized whole, not split into ASCII fragments
        var typo = SearchSpellChecker.SuggestCorrections(word.Replace("ü", "u").Replace("û", "u"), vocab);
        Assert.Equal(word, typo.CorrectedQuery);
    }

    [Fact]
    public void Cosine_UnitVectorsEqualDot_AndUnnormalizedStillExact()
    {
        var a = new float[] { 0.6f, 0.8f }; var b = new float[] { 0.8f, 0.6f };
        Assert.Equal(0.96, HybridSmartSearchService.Cosine(a, b), 4);
        Assert.Equal(0.96, HybridSmartSearchService.Cosine(new float[] { 6, 8 }, new float[] { 8, 6 }), 4);
        Assert.Equal(0, HybridSmartSearchService.Cosine(new float[] { 0, 0 }, b));
    }

    [Fact]
    public void ThirdPartyNotices_CreditsTatarize()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("Copyright (c) 2018 Tatarize and pyembroidery contributors", text);
    }

    [Fact]
    public void InspectPathVolume_InternalMntIsNotRemovable()
    {
        UsbDriveService.VolumeProvider = null;
        var v = UsbDriveService.InspectPathVolume("/mnt/internal_data/design.pes");
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Assert.False(v.IsRemovable);
        Assert.False(UsbDriveService.InspectPathVolume("/mnt/internal_data/design.pes", Array.Empty<(string, string, string)>()).IsRemovable);
    }
}
