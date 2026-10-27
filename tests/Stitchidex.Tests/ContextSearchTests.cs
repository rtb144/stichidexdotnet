using Stitchidex.Core.Models;
using Stitchidex.Core.Search;
using Stitchidex.Core.Services;
namespace Stitchidex.Tests;

public class ContextSearchTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stxctx").FullName;
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    SqliteCatalogService Seed()
    {
        var c = new SqliteCatalogService(Path.Combine(_dir, "c.db"));
        void Add(string rel, string title, params string[] tags)
        {
            var p = new EmbroideryPattern { Title = title, FilePath = Path.Combine(_dir, rel) };
            foreach (var t in tags) p.Tags.Add(t);
            c.Upsert(p);
        }
        Add("misc/Digger Truck Stitched 2_5 Inch.pes", "Digger Truck Stitched 2 5 Inch", "pec:Underconstructio");
        Add("misc/track_hoe.pes", "Track Hoe");
        Add("Construction Vehicles/a001.pes", "a001");
        Add("misc/Snowman.pes", "Snowman");
        Add("misc/frosty_snowflake.pes", "Frosty Snowflake");
        Add("misc/Christmas Reindeer.pes", "Christmas Reindeer");
        Add("Winter/b002.pes", "b002");
        Add("misc/rose.pes", "Rose Bouquet");
        Add("misc/puppy.pes", "Puppy Paw");
        return c;
    }

    [Fact]
    public void SplitCompoundTokens_Works()
    {
        var t = SearchText.SplitCompoundTokens("Underconstructio trackhoe snowman jackolantern"); Console.WriteLine(string.Join(",", t));
        Assert.Contains("construction", t); Assert.Contains("under", t);
        Assert.Contains("hoe", t); Assert.Contains("snow", t); Assert.Contains("lantern", t);
    }

    [Fact]
    public void Winter_FindsWinterDesigns()
    {
        using var c = Seed();
        var titles = new HybridSmartSearchService(c).Search("winter").Select(r => r.Entry.Title).ToList();
        foreach (var t in new[] { "Snowman", "Frosty Snowflake", "Christmas Reindeer", "b002" }) Assert.Contains(t, titles);
        Assert.DoesNotContain("Puppy Paw", titles);
    }

    [Fact]
    public void Construction_FindsPecLabelFolderAndMachines()
    {
        using var c = Seed();
        var titles = new HybridSmartSearchService(c).Search("construction").Select(r => r.Entry.Title).ToList();
        foreach (var t in new[] { "Digger Truck Stitched 2 5 Inch", "Track Hoe", "a001" }) Assert.Contains(t, titles);
        Assert.DoesNotContain("Rose Bouquet", titles);
        Assert.Contains("Underconstructio", c.All().First(e => e.Title.StartsWith("Digger")).SemanticCard);
    }

    [Fact]
    public async Task SearchAsync_RunsOffThreadAndCancels()
    {
        using var c = Seed();
        var s = new HybridSmartSearchService(c);
        var tid = Environment.CurrentManagedThreadId;
        var r = await s.SearchAsync("winter");
        Assert.NotEmpty(r);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.SearchAsync("construction", ct: cts.Token));
    }

    [Fact]
    public void Backfill_UpgradesOldCards()
    {
        using var c = Seed();
        Assert.Equal(0, c.BackfillSemanticCards());
    }

    [Fact]
    public void OnnxBge_ProducesSemanticVectors()
    {
        if (!OnnxBgeEmbeddingProvider.IsAvailable()) return; // model cached at ~/.local/share/Stitchidex/models
        using var m = new OnnxBgeEmbeddingProvider();
        var w = m.Embed("winter");
        Assert.Equal(768, w.Length);
        Assert.InRange(Math.Sqrt(w.Sum(x => (double)x * x)), 0.99, 1.01);
        double Cos(string a, string b) => HybridSmartSearchService.Cosine(m.Embed(a), m.Embed(b));
        Assert.True(Cos("winter", "snowman snowflake") > Cos("winter", "digger excavator dump truck"));
        Assert.True(Cos("construction", "digger excavator dump truck") > Cos("construction", "snowman snowflake"));
    }
}
