using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Stitchidex.Core.Models;
using Stitchidex.Core.Parsers;
using Stitchidex.Core.Rendering;
using Stitchidex.Core.Search;
namespace Stitchidex.Core.Services;

public sealed record CatalogEntry(long Id, string Path, string Title, string FormatFamily, int StitchCount, int ColorCount,
    double WidthMm, double HeightMm, string HoopCategory, string SemanticCard, IReadOnlyList<string> Tags, string Notes, byte[]? Thumbnail)
{
    /// <summary>Thread palette as #RRGGBB hex codes (cached at scan time so cards never re-parse files).</summary>
    public IReadOnlyList<string> PaletteHex { get; init; } = Array.Empty<string>();
    public int JumpCount { get; init; }
    public int TrimCount { get; init; }
    public bool IsFromRemovableDrive { get; init; }
    public string VolumeLabel { get; init; } = "";
    public string VolumeSerial { get; init; } = "";
    public string VolumeRelativePath { get; init; } = "";
    /// <summary>True when the original file bytes are cached in SQLite (works with the USB stick unplugged).</summary>
    public bool HasCachedBlob { get; init; }
}

public sealed class SqliteCatalogService : IDisposable
{
    static readonly HashSet<string> SkipExts = new(StringComparer.OrdinalIgnoreCase) { ".py", ".toml", ".txt", ".csv", ".json", ".md", ".edr", ".col", ".inf", ".png", ".jpg" };
    readonly SqliteConnection _db;
    readonly object _gate = new();
    SqliteTransaction? _tx;
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
          CREATE VIRTUAL TABLE IF NOT EXISTS patterns_fts USING fts5(title, semantic_card, tags, notes, content='patterns', content_rowid='id');
          CREATE TABLE IF NOT EXISTS embeddings(pattern_id INTEGER PRIMARY KEY, model TEXT, dim INTEGER, vector BLOB);");
        foreach (var col in new[] { "palette TEXT", "jump_count INTEGER DEFAULT 0", "trim_count INTEGER DEFAULT 0", "is_removable INTEGER DEFAULT 0", "volume_label TEXT DEFAULT ''", "volume_serial TEXT DEFAULT ''", "volume_rel_path TEXT DEFAULT ''", "raw_blob BLOB", "has_cached_blob INTEGER DEFAULT 0" })
            try { Exec("ALTER TABLE patterns ADD COLUMN " + col); } catch (SqliteException) { }
        MigrateFtsNotesColumn();
        PurgeNonPatternRecords();
    }

    long _version;
    /// <summary>Incremented on every write; lets search caches invalidate cheaply.</summary>
    public long Version => Interlocked.Read(ref _version);

    int Exec(string sql, params (string, object?)[] ps)
    {
        lock (_gate)
        {
        Interlocked.Increment(ref _version);
        using var c = _db.CreateCommand(); c.CommandText = sql; c.Transaction = _tx;
        foreach (var (k, v) in ps) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return c.ExecuteNonQuery();
        }
    }

    /// <summary>Runs <paramref name="body"/> inside one SQLite transaction (thread-safe, reentrant on same thread).</summary>
    void InTransaction(Action body)
    {
        lock (_gate)
        {
            if (_tx != null) { body(); return; }
            _tx = _db.BeginTransaction();
            try { body(); _tx.Commit(); }
            catch { _tx.Rollback(); throw; }
            finally { _tx.Dispose(); _tx = null; }
        }
    }

    void ReindexFts() => Exec("INSERT INTO patterns_fts(patterns_fts) VALUES('rebuild');");

    /// <summary>Older catalogs created patterns_fts without the notes column; recreate + rebuild once.</summary>
    void MigrateFtsNotesColumn()
    {
        string? sql;
        lock (_gate)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT sql FROM sqlite_master WHERE name='patterns_fts'";
            sql = c.ExecuteScalar() as string;
        }
        if (sql != null && !sql.Contains("notes", StringComparison.OrdinalIgnoreCase))
        {
            Exec("DROP TABLE patterns_fts;");
            Exec("CREATE VIRTUAL TABLE patterns_fts USING fts5(title, semantic_card, tags, notes, content='patterns', content_rowid='id');");
            ReindexFts();
        }
    }

    (string Title, string Card, string Tags, string Notes)? FtsValues(long id)
    {
        lock (_gate)
        {
            using var c = _db.CreateCommand(); c.Transaction = _tx;
            c.CommandText = "SELECT title, semantic_card, tags, notes FROM patterns WHERE id=$i";
            c.Parameters.AddWithValue("$i", id);
            using var r = c.ExecuteReader();
            if (!r.Read()) return null;
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            return (S(0), S(1), S(2), S(3));
        }
    }

    /// <summary>Targeted single-row FTS5 update: removes the row's old indexed values and inserts the current ones
    /// (no full-table 'rebuild' on every tag/note edit).</summary>
    void UpdateFtsRow(long id, (string Title, string Card, string Tags, string Notes)? old)
    {
        lock (_gate)
        {
            if (old is { } o)
                Exec("INSERT INTO patterns_fts(patterns_fts,rowid,title,semantic_card,tags,notes) VALUES('delete',$i,$t,$c,$g,$n)",
                    ("$i", id), ("$t", o.Title), ("$c", o.Card), ("$g", o.Tags), ("$n", o.Notes));
            if (FtsValues(id) is { } v)
                Exec("INSERT INTO patterns_fts(rowid,title,semantic_card,tags,notes) VALUES($i,$t,$c,$g,$n)",
                    ("$i", id), ("$t", v.Title), ("$c", v.Card), ("$g", v.Tags), ("$n", v.Notes));
        }
    }

    /// <summary>Applies a metadata edit to one row and updates only that row's FTS entry.</summary>
    void UpdateMeta(long id, Action edit)
    {
        lock (_gate)
        {
            var old = FtsValues(id);
            edit();
            UpdateFtsRow(id, old);
        }
    }

    public int PurgeNonPatternRecords()
    {
        var bad = AllMetadataRaw().Where(e => !EmbroideryParserRegistry.IsEmbroideryExtension(System.IO.Path.GetExtension(e.Path))).Select(e => e.Id).ToList();
        foreach (var id in bad) { Exec("DELETE FROM patterns WHERE id=$i", ("$i", id)); Exec("DELETE FROM embeddings WHERE pattern_id=$i", ("$i", id)); }
        if (bad.Count > 0) ReindexFts();
        return bad.Count;
    }

    public static bool IsPatternFile(string f)
    {
        var ext = System.IO.Path.GetExtension(f);
        return !SkipExts.Contains(ext) && EmbroideryParserRegistry.IsEmbroideryExtension(ext);
    }

    static EmbroideryPattern? ParseAndRender(string f, bool render)
    {
        try
        {
            var p = EmbroideryParserRegistry.ParseFile(f);
            if (render && p.StitchCount > 0) p.PngThumbnail = ThumbnailRenderer.RenderThumbnailPng(p);
            return p;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Any corrupt/hostile file (Overflow, InvalidCast, NullReference, OOM, ...) skips just this file, never the whole chunk.
            System.Diagnostics.Trace.TraceWarning($"Stitchidex: skipped unreadable pattern '{f}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public int ScanDirectory(string folder, bool renderThumbnails = true) =>
        ScanDirectoryAsync(folder, null, default, renderThumbnails).GetAwaiter().GetResult();

    /// <summary>Background scan: enumerates first (Total known), parses + renders in parallel, writes in batched transactions.</summary>
    public Task<int> ScanDirectoryAsync(string folder, IProgress<(int Done, int Total, string FileName)>? progress = null,
        CancellationToken ct = default, bool renderThumbnails = true) => Task.Run(() =>
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(IsPatternFile).ToList();
        return IndexFiles(files, progress, ct, renderThumbnails);
    }, ct);

    int IndexFiles(IReadOnlyList<string> files, IProgress<(int Done, int Total, string FileName)>? progress, CancellationToken ct, bool render)
    {
        int total = files.Count, done = 0, indexed = 0;
        progress?.Report((0, total, ""));
        const int Chunk = 64;
        var opts = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
        for (int i = 0; i < total; i += Chunk)
        {
            ct.ThrowIfCancellationRequested();
            var batch = files.Skip(i).Take(Chunk).ToList();
            var parsed = new EmbroideryPattern?[batch.Count];
            Parallel.For(0, batch.Count, opts, k =>
            {
                parsed[k] = ParseAndRender(batch[k], render);
                var d = Interlocked.Increment(ref done);
                if (d % Math.Max(1, total / 200) == 0) progress?.Report((d, total, System.IO.Path.GetFileName(batch[k]))); // throttled: never floods the UI thread
            });
            InTransaction(() => { foreach (var p in parsed) if (p != null) { Upsert(p); indexed++; } });
        }
        ReindexFts();
        progress?.Report((total, total, ""));
        return indexed;
    }

    /// <summary>Re-parse and render any catalog rows whose thumbnail BLOB is NULL/empty (e.g. from older parser versions).</summary>
    public Task<int> RepairMissingThumbnailsAsync(IProgress<(int Done, int Total, string FileName)>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var files = Query("SELECT " + Cols + " FROM patterns WHERE palette IS NULL OR ((thumbnail IS NULL OR length(thumbnail)=0) AND stitch_count > 0)")
                .Select(e => e.Path).Where(File.Exists).ToList();
            return files.Count == 0 ? 0 : IndexFiles(files, progress, ct, true);
        }, ct);

    public void Upsert(EmbroideryPattern p)
    {
        var full = System.IO.Path.GetFullPath(p.FilePath);
        var labels = p.Tags.Where(t => t.StartsWith("pec:")).Select(t => t[4..]).ToList();
        var tags = p.Tags.Where(t => !t.Contains(':')).ToList();
        var card = SearchText.BuildCard(p.Title, full, labels, tags);
        Exec("DELETE FROM embeddings WHERE pattern_id=(SELECT id FROM patterns WHERE path=$p)", ("$p", full));
        Exec(@"INSERT INTO patterns(path,title,format_family,stitch_count,color_count,width_mm,height_mm,hoop_category,thumbnail,semantic_card,tags,palette,jump_count,trim_count)
        VALUES($p,$t,$f,$s,$c,$w,$h,$hc,$th,$sc,$tg,$pl,$j,$tr)
        ON CONFLICT(path) DO UPDATE SET title=$t,format_family=$f,stitch_count=$s,color_count=$c,width_mm=$w,height_mm=$h,hoop_category=$hc,thumbnail=$th,semantic_card=$sc,palette=$pl,jump_count=$j,trim_count=$tr",
        ("$p", full), ("$t", p.Title), ("$f", p.FormatFamily), ("$s", p.StitchCount), ("$c", p.Colors.Count),
        ("$w", p.WidthMm), ("$h", p.HeightMm), ("$hc", p.HoopCategory), ("$th", p.PngThumbnail), ("$sc", card),
        ("$tg", JsonSerializer.Serialize(tags)),
        ("$pl", JsonSerializer.Serialize(p.Colors.Select(c => c.Hex).ToList())), ("$j", p.JumpCount), ("$tr", p.TrimCount));
        var vol = UsbDriveService.InspectPathVolume(full);
        byte[]? blob = null;
        if (vol.IsRemovable)
            try { if (new FileInfo(full).Length <= UsbDriveService.MaxCachedBytes) blob = UsbDriveService.ReadAllBytesShared(full); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Exec("UPDATE patterns SET is_removable=$r,volume_label=$vl,volume_serial=$vs,volume_rel_path=$vr,raw_blob=$b,has_cached_blob=$hb WHERE path=$p",
            ("$r", vol.IsRemovable ? 1 : 0), ("$vl", vol.VolumeLabel), ("$vs", vol.VolumeSerial), ("$vr", vol.VolumeRelativePath),
            ("$b", blob), ("$hb", blob is { Length: > 0 } ? 1 : 0), ("$p", full));
    }

    /// <summary>Original file bytes cached for removable-drive patterns (null when not cached).</summary>
    public byte[]? GetCachedPatternBytes(long id)
    {
        lock (_gate)
        {
            using var c = _db.CreateCommand(); c.Transaction = _tx;
            c.CommandText = "SELECT raw_blob FROM patterns WHERE id=$i"; c.Parameters.AddWithValue("$i", id);
            return c.ExecuteScalar() as byte[];
        }
    }

    /// <summary>Upgrades old rows: rebuilds semantic cards with folder context + PEC header label (no DB delete needed).</summary>
    public int BackfillSemanticCards()
    {
        int n = 0;
        var rows = Query("SELECT id,path,title,format_family,stitch_count,color_count,width_mm,height_mm,hoop_category,NULL AS thumbnail,semantic_card,tags,notes,palette,jump_count,trim_count FROM patterns WHERE semantic_card IS NULL OR instr(semantic_card,' || ')=0");
        InTransaction(() =>
        {
            foreach (var e in rows)
            {
                var label = SearchText.ReadPecLabel(e.Path);
                var card = SearchText.BuildCard(e.Title, e.Path, label is null ? Array.Empty<string>() : new[] { label }, e.Tags, e.Notes);
                Exec("UPDATE patterns SET semantic_card=$c WHERE id=$i", ("$c", card), ("$i", e.Id));
                Exec("DELETE FROM embeddings WHERE pattern_id=$i", ("$i", e.Id));
                n++;
            }
        });
        if (n > 0) ReindexFts();
        return n;
    }

    /// <summary>All stored vectors for a model in one query.</summary>
    public Dictionary<long, float[]> GetAllEmbeddings(string model)
    {
        lock (_gate)
        {
            using var c = _db.CreateCommand(); c.Transaction = _tx;
            c.CommandText = "SELECT pattern_id, vector FROM embeddings WHERE model=$m";
            c.Parameters.AddWithValue("$m", model);
            var d = new Dictionary<long, float[]>();
            using var r = c.ExecuteReader();
            while (r.Read()) { var b = (byte[])r[1]; var v = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, v, 0, b.Length); d[r.GetInt64(0)] = v; }
            return d;
        }
    }

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

    public CatalogEntry? Get(long id) => Query("SELECT " + Cols + " FROM patterns WHERE id=$i", ("$i", id)).FirstOrDefault();
    public IReadOnlyList<CatalogEntry> All() => Query("SELECT " + Cols + " FROM patterns ORDER BY title COLLATE NOCASE");
    IReadOnlyList<CatalogEntry> AllRaw() => Query("SELECT " + Cols + " FROM patterns");

    /// <summary>Lightweight metadata query: never reads the thumbnail PNG or raw_blob BLOB columns.</summary>
    public IReadOnlyList<CatalogEntry> AllMetadataRaw() => Query("SELECT " + MetaCols + " FROM patterns");
    CatalogEntry? GetMeta(long id) => Query("SELECT " + MetaCols + " FROM patterns WHERE id=$i", ("$i", id)).FirstOrDefault();

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
        InTransaction(() => { foreach (var id in ids) if (GetMeta(id) is { } e) UpdateMeta(id, () => SetTags(id, e.Tags.Concat(add))); });
    }

    public void BulkRemoveTags(IEnumerable<long> ids, IEnumerable<string> tags)
    {
        var rm = new HashSet<string>(tags.Select(Norm));
        InTransaction(() => { foreach (var id in ids) if (GetMeta(id) is { } e) UpdateMeta(id, () => SetTags(id, e.Tags.Where(t => !rm.Contains(t)))); });
    }

    public void SetNotes(long id, string notes) => UpdateMeta(id, () => Exec("UPDATE patterns SET notes=$n WHERE id=$i", ("$n", notes), ("$i", id)));

    public IReadOnlyList<string> GetAllCraftTags() =>
        AllMetadataRaw().SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();

    public IReadOnlyList<(long Id, double Rank)> FullTextSearch(string query, int limit = 200)
    {
        var terms = Regex.Matches(query, @"[\p{L}\p{N}]+").Select(m => "\"" + m.Value + "\"*").ToList();
        if (terms.Count == 0) return Array.Empty<(long, double)>();
        lock (_gate)
        {
        using var c = _db.CreateCommand(); c.Transaction = _tx;
        c.CommandText = "SELECT rowid, bm25(patterns_fts) FROM patterns_fts WHERE patterns_fts MATCH $q ORDER BY 2 LIMIT $l";
        c.Parameters.AddWithValue("$q", string.Join(" OR ", terms)); c.Parameters.AddWithValue("$l", limit);
        var r = new List<(long, double)>();
        using var rd = c.ExecuteReader();
        while (rd.Read()) r.Add((rd.GetInt64(0), rd.GetDouble(1)));
        return r;
        }
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
        lock (_gate)
        {
        using var c = _db.CreateCommand(); c.Transaction = _tx;
        c.CommandText = "SELECT vector FROM embeddings WHERE pattern_id=$i AND model=$m";
        c.Parameters.AddWithValue("$i", id); c.Parameters.AddWithValue("$m", model);
        if (c.ExecuteScalar() is not byte[] b) return null;
        var v = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, v, 0, b.Length); return v;
        }
    }

    const string Cols = "id,path,title,format_family,stitch_count,color_count,width_mm,height_mm,hoop_category,thumbnail,semantic_card,tags,notes,palette,jump_count,trim_count,is_removable,volume_label,volume_serial,volume_rel_path,has_cached_blob";
    const string MetaCols = "id,path,title,format_family,stitch_count,color_count,width_mm,height_mm,hoop_category,NULL AS thumbnail,semantic_card,tags,notes,palette,jump_count,trim_count,is_removable,volume_label,volume_serial,volume_rel_path,CASE WHEN raw_blob IS NOT NULL AND length(raw_blob) > 0 THEN 1 ELSE 0 END AS has_cached_blob";

    IReadOnlyList<CatalogEntry> Query(string sql, params (string, object?)[] ps)
    {
        lock (_gate) return QueryCore(sql, ps);
    }

    IReadOnlyList<CatalogEntry> QueryCore(string sql, (string, object?)[] ps)
    {
        using var c = _db.CreateCommand(); c.CommandText = sql; c.Transaction = _tx;
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
                tags, S("notes"), r["thumbnail"] as byte[])
            {
                PaletteHex = ReadList(S("palette")),
                JumpCount = r["jump_count"] is long jc ? (int)jc : 0,
                TrimCount = r["trim_count"] is long tc ? (int)tc : 0,
                IsFromRemovableDrive = Has(r, "is_removable") && r["is_removable"] is long ir && ir != 0,
                VolumeLabel = Has(r, "volume_label") ? S("volume_label") : "",
                VolumeSerial = Has(r, "volume_serial") ? S("volume_serial") : "",
                VolumeRelativePath = Has(r, "volume_rel_path") ? S("volume_rel_path") : "",
                HasCachedBlob = Has(r, "has_cached_blob") && r["has_cached_blob"] is long hb && hb != 0,
            });
        }
        return list;
    }

    static bool Has(SqliteDataReader r, string col) { for (int i = 0; i < r.FieldCount; i++) if (r.GetName(i) == col) return true; return false; }

    static List<string> ReadList(string json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); } catch (JsonException) { return new(); }
    }

    public void Dispose() => _db.Dispose();
}
