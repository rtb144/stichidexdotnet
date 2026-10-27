using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Stitchidex.Core.Models;
using Stitchidex.Core.Parsers;
using Stitchidex.Core.Rendering;
namespace Stitchidex.Core.Services;

public sealed record CatalogEntry(long Id, string Path, string Title, string FormatFamily, int StitchCount, int ColorCount,
    double WidthMm, double HeightMm, string HoopCategory, string SemanticCard, IReadOnlyList<string> Tags, string Notes, byte[]? Thumbnail);

public sealed class SqliteCatalogService : IDisposable
{
    static readonly HashSet<string> SkipExts = new(StringComparer.OrdinalIgnoreCase) { ".py", ".toml", ".txt", ".csv", ".json", ".md", ".edr", ".col", ".inf", ".png", ".jpg" };
    readonly SqliteConnection _db;
    public string DatabasePath { get; }

    public SqliteCatalogService(string dbPath)
    {
        DatabasePath = dbPath;
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec(@"CREATE TABLE IF NOT EXISTS patterns(
            id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT UNIQUE NOT NULL, title TEXT, format_family TEXT,
            stitch_count INTEGER, color_count INTEGER, width_mm REAL, height_mm REAL, hoop_category TEXT,
            thumbnail BLOB, semantic_card TEXT, tags TEXT DEFAULT '[]', notes TEXT DEFAULT '');
          CREATE VIRTUAL TABLE IF NOT EXISTS patterns_fts USING fts5(title, semantic_card, tags, content='patterns', content_rowid='id');
          CREATE TABLE IF NOT EXISTS embeddings(pattern_id INTEGER PRIMARY KEY, model TEXT, dim INTEGER, vector BLOB);");
        PurgeNonPatternRecords();
    }

    int Exec(string sql, params (string, object?)[] ps)
    {
        using var c = _db.CreateCommand(); c.CommandText = sql;
        foreach (var (k, v) in ps) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return c.ExecuteNonQuery();
    }

    void ReindexFts() => Exec("INSERT INTO patterns_fts(patterns_fts) VALUES('rebuild');");

    public int PurgeNonPatternRecords()
    {
        var bad = AllRaw().Where(e => !EmbroideryParserRegistry.IsEmbroideryExtension(System.IO.Path.GetExtension(e.Path))).Select(e => e.Id).ToList();
        foreach (var id in bad) { Exec("DELETE FROM patterns WHERE id=$i", ("$i", id)); Exec("DELETE FROM embeddings WHERE pattern_id=$i", ("$i", id)); }
        if (bad.Count > 0) ReindexFts();
        return bad.Count;
    }

    public int ScanDirectory(string folder, bool renderThumbnails = true)
    {
        int n = 0;
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var ext = System.IO.Path.GetExtension(f);
            if (SkipExts.Contains(ext) || !EmbroideryParserRegistry.IsEmbroideryExtension(ext)) continue;
            EmbroideryPattern p;
            try { p = EmbroideryParserRegistry.ParseFile(f); } catch { continue; }
            if (renderThumbnails && p.Stitches.Count > 0) p.PngThumbnail = ThumbnailRenderer.RenderThumbnailPng(p);
            Upsert(p); n++;
        }
        ReindexFts();
        return n;
    }

    public void Upsert(EmbroideryPattern p) => Exec(@"INSERT INTO patterns(path,title,format_family,stitch_count,color_count,width_mm,height_mm,hoop_category,thumbnail,semantic_card,tags)
        VALUES($p,$t,$f,$s,$c,$w,$h,$hc,$th,$sc,$tg)
        ON CONFLICT(path) DO UPDATE SET title=$t,format_family=$f,stitch_count=$s,color_count=$c,width_mm=$w,height_mm=$h,hoop_category=$hc,thumbnail=$th,semantic_card=$sc",
        ("$p", System.IO.Path.GetFullPath(p.FilePath)), ("$t", p.Title), ("$f", p.FormatFamily), ("$s", p.StitchCount), ("$c", p.Colors.Count),
        ("$w", p.WidthMm), ("$h", p.HeightMm), ("$hc", p.HoopCategory), ("$th", p.PngThumbnail), ("$sc", p.SemanticCard),
        ("$tg", JsonSerializer.Serialize(p.Tags.Where(t => !t.Contains(':')).ToList())));

    public int RemoveDirectoryRecords(string folder)
    {
        var prefix = System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        Exec("DELETE FROM embeddings WHERE pattern_id IN (SELECT id FROM patterns WHERE substr(path,1,length($p))=$p)", ("$p", prefix));
        int n = Exec("DELETE FROM patterns WHERE substr(path,1,length($p))=$p", ("$p", prefix));
        ReindexFts();
        return n;
    }

    /// <summary>Replace 16-char PEC-truncated titles with the descriptive filename stem.</summary>
    public int RepairTruncatedTitles()
    {
        int n = 0;
        foreach (var e in AllRaw())
        {
            var stem = Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(e.Path), @"[_\-\.]+", " ").Trim();
            if (e.Title.Length <= 16 && stem.Length > e.Title.Length && stem.StartsWith(e.Title.Trim(), StringComparison.OrdinalIgnoreCase))
            { Exec("UPDATE patterns SET title=$t WHERE id=$i", ("$t", stem), ("$i", e.Id)); n++; }
        }
        if (n > 0) ReindexFts();
        return n;
    }

    public CatalogEntry? Get(long id) => Query("SELECT * FROM patterns WHERE id=$i", ("$i", id)).FirstOrDefault();
    public IReadOnlyList<CatalogEntry> All() => Query("SELECT * FROM patterns ORDER BY title COLLATE NOCASE");
    IReadOnlyList<CatalogEntry> AllRaw() => Query("SELECT * FROM patterns");

    void SetTags(long id, IEnumerable<string> tags)
    {
        Exec("UPDATE patterns SET tags=$t WHERE id=$i", ("$t", JsonSerializer.Serialize(tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList())), ("$i", id));
    }
    static string Norm(string t) => t.Trim().ToLowerInvariant();

    public void AddTag(long id, string tag) => BulkAddTags(new[] { id }, new[] { tag });
    public void RemoveTag(long id, string tag) => BulkRemoveTags(new[] { id }, new[] { tag });

    public void BulkAddTags(IEnumerable<long> ids, IEnumerable<string> tags)
    {
        var add = tags.Select(Norm).Where(t => t.Length > 0).ToList();
        using var tx = _db.BeginTransaction();
        foreach (var id in ids) if (Get(id) is { } e) SetTags(id, e.Tags.Concat(add));
        tx.Commit(); ReindexFts();
    }

    public void BulkRemoveTags(IEnumerable<long> ids, IEnumerable<string> tags)
    {
        var rm = new HashSet<string>(tags.Select(Norm));
        using var tx = _db.BeginTransaction();
        foreach (var id in ids) if (Get(id) is { } e) SetTags(id, e.Tags.Where(t => !rm.Contains(t)));
        tx.Commit(); ReindexFts();
    }

    public void SetNotes(long id, string notes) => Exec("UPDATE patterns SET notes=$n WHERE id=$i", ("$n", notes), ("$i", id));

    public IReadOnlyList<string> GetAllCraftTags() =>
        AllRaw().SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();

    public IReadOnlyList<(long Id, double Rank)> FullTextSearch(string query, int limit = 200)
    {
        var terms = Regex.Matches(query, @"[\p{L}\p{N}]+").Select(m => "\"" + m.Value + "\"*").ToList();
        if (terms.Count == 0) return Array.Empty<(long, double)>();
        using var c = _db.CreateCommand();
        c.CommandText = "SELECT rowid, bm25(patterns_fts) FROM patterns_fts WHERE patterns_fts MATCH $q ORDER BY 2 LIMIT $l";
        c.Parameters.AddWithValue("$q", string.Join(" OR ", terms)); c.Parameters.AddWithValue("$l", limit);
        var r = new List<(long, double)>();
        using var rd = c.ExecuteReader();
        while (rd.Read()) r.Add((rd.GetInt64(0), rd.GetDouble(1)));
        return r;
    }

    public IReadOnlyList<CatalogEntry> Search(string query) =>
        FullTextSearch(query).Select(h => Get(h.Id)).Where(e => e != null).Select(e => e!).ToList();

    public void SaveEmbedding(long id, string model, float[] v)
    {
        var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length);
        Exec("INSERT OR REPLACE INTO embeddings(pattern_id,model,dim,vector) VALUES($i,$m,$d,$v)", ("$i", id), ("$m", model), ("$d", v.Length), ("$v", b));
    }

    public float[]? GetEmbedding(long id, string model)
    {
        using var c = _db.CreateCommand();
        c.CommandText = "SELECT vector FROM embeddings WHERE pattern_id=$i AND model=$m";
        c.Parameters.AddWithValue("$i", id); c.Parameters.AddWithValue("$m", model);
        if (c.ExecuteScalar() is not byte[] b) return null;
        var v = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, v, 0, b.Length); return v;
    }

    IReadOnlyList<CatalogEntry> Query(string sql, params (string, object?)[] ps)
    {
        using var c = _db.CreateCommand(); c.CommandText = sql;
        foreach (var (k, v) in ps) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var list = new List<CatalogEntry>();
        using var r = c.ExecuteReader();
        string S(string n) => r[n] is string s ? s : "";
        while (r.Read())
        {
            List<string> tags;
            try { tags = JsonSerializer.Deserialize<List<string>>(S("tags")) ?? new(); } catch { tags = new(); }
            list.Add(new CatalogEntry(r.GetInt64(r.GetOrdinal("id")), S("path"), S("title"), S("format_family"),
                r["stitch_count"] is long sc ? (int)sc : 0, r["color_count"] is long cc ? (int)cc : 0,
                r["width_mm"] is double w ? w : 0, r["height_mm"] is double h ? h : 0, S("hoop_category"), S("semantic_card"),
                tags, S("notes"), r["thumbnail"] as byte[]));
        }
        return list;
    }

    public void Dispose() => _db.Dispose();
}
