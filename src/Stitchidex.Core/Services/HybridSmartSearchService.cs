using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Stitchidex.Core.Search;
namespace Stitchidex.Core.Services;

public sealed record SmartSearchResult(CatalogEntry Entry, double Score, double Lexical, double Semantic);

/// <summary>Hybrid lexical + concept + vector search over an in-memory cache (no SQLite reads per keystroke).</summary>
public sealed class HybridSmartSearchService
{
    public const double NoiseFloor = 0.22;
    readonly SqliteCatalogService _catalog;
    IEmbeddingProvider _embedder;
    readonly object _lock = new();
    readonly ConcurrentDictionary<long, float[]> _vectors = new();
    readonly ConcurrentDictionary<string, float[]> _queryCache = new();
    IReadOnlyList<CatalogEntry> _entries = Array.Empty<CatalogEntry>();
    Dictionary<long, (HashSet<string> Tokens, HashSet<int> Concepts, string Text)> _docs = new();
    long _loadedVersion = -1;

    public HybridSmartSearchService(SqliteCatalogService catalog, IEmbeddingProvider? embedder = null)
    { _catalog = catalog; _embedder = embedder ?? new LocalCpuEmbeddingProvider(); }

    public string Status => _embedder.Status;
    public IEmbeddingProvider Embedder => _embedder;

    /// <summary>Swap embedder (e.g. once bge-base finishes downloading); vectors are recomputed lazily.</summary>
    public void SetEmbedder(IEmbeddingProvider e) { lock (_lock) { _embedder = e; _vectors.Clear(); _queryCache.Clear(); _loadedVersion = -1; } }

    public static string ExpandQuery(string q)
    {
        var toks = SearchText.SplitCompoundTokens(q);
        var extra = SearchText.Concepts(toks).SelectMany(i => SearchText.Groups[i]);
        return string.Join(" ", toks.Concat(extra).Distinct());
    }

    public static double Cosine(float[] a, float[] b)
    {
        double d = 0, na = 0, nb = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : d / Math.Sqrt(na * nb);
    }

    internal static string Doc(CatalogEntry e)
    {
        var card = e.SemanticCard.Contains(" || ") ? e.SemanticCard : SearchText.BuildCard(e.Title, e.Path, Array.Empty<string>(), e.Tags, e.Notes);
        return $"{card} {string.Join(' ', e.Tags)} {e.Notes}";
    }

    /// <summary>Refresh in-memory cache if catalog changed; embeds missing docs. Safe to call from a background thread.</summary>
    public void EnsureIndex(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var ver = _catalog.Version;
            if (ver != _loadedVersion)
            {
                _entries = _catalog.All();
                _docs = _entries.ToDictionary(e => e.Id, e =>
                {
                    var text = Doc(e);
                    var toks = SearchText.SplitCompoundTokens(text).ToHashSet();
                    return (toks, SearchText.Concepts(toks).ToHashSet(), text.ToLowerInvariant());
                });
                foreach (var kv in _catalog.GetAllEmbeddings(_embedder.ModelName)) _vectors[kv.Key] = kv.Value;
                foreach (var id in _vectors.Keys.Where(k => !_docs.ContainsKey(k)).ToList()) _vectors.TryRemove(id, out _);
                _loadedVersion = ver;
            }
            var missing = _entries.Where(e => !_vectors.ContainsKey(e.Id)).ToList();
            foreach (var e in missing)
            {
                ct.ThrowIfCancellationRequested();
                var v = _embedder.Embed(Doc(e));
                _vectors[e.Id] = v;
                _catalog.SaveEmbedding(e.Id, _embedder.ModelName, v);
            }
            if (missing.Count > 0) _loadedVersion = _catalog.Version; // our own SaveEmbedding writes don't invalidate
        }
    }

    public IReadOnlyList<CatalogEntry> Entries { get { EnsureIndexMeta(); return _entries; } }
    void EnsureIndexMeta() { if (_catalog.Version != _loadedVersion) EnsureIndex(); }

    public void IndexEmbeddings() => EnsureIndex();

    public Task<IReadOnlyList<SmartSearchResult>> SearchAsync(string query, int limit = 500, CancellationToken ct = default) =>
        Task.Run(() => Search(query, limit, ct), ct);

    public IReadOnlyList<SmartSearchResult> Search(string query, int limit = 500, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SmartSearchResult>();
        EnsureIndex(ct);
        var qToks = SearchText.SplitCompoundTokens(query);
        var qWords = Regex.Matches(query.ToLowerInvariant(), @"[a-z0-9]+").Select(m => m.Value).ToList();
        var qConcepts = SearchText.Concepts(qToks).ToHashSet();
        var expanded = ExpandQuery(query);
        var qv = _queryCache.GetOrAdd(query.Trim().ToLowerInvariant(), _ => _embedder.Embed(query + " " + expanded));
        if (_queryCache.Count > 256) _queryCache.Clear();
        var results = new List<SmartSearchResult>();
        foreach (var e in _entries)
        {
            ct.ThrowIfCancellationRequested();
            var d = _docs[e.Id];
            double wordHit = qWords.Count == 0 ? 0 : qWords.Count(w => d.Tokens.Contains(w) || d.Tokens.Contains(w.TrimEnd('s')) || (w.Length >= 4 && d.Text.Contains(w))) / (double)qWords.Count;
            double titleHit = qWords.Count == 0 ? 0 : qWords.Count(w => e.Title.Contains(w, StringComparison.OrdinalIgnoreCase)) / (double)qWords.Count;
            double concept = qConcepts.Count == 0 ? 0 : qConcepts.Count(c => d.Concepts.Contains(c)) / (double)qConcepts.Count;
            double lex = 0.30 * wordHit + 0.15 * titleHit + 0.30 * concept;
            double sem = _vectors.TryGetValue(e.Id, out var ev) ? Math.Max(0, Cosine(qv, ev)) : 0;
            double score = Math.Min(1.0, 0.45 * sem + lex);
            if (score >= NoiseFloor && (lex > 0 || sem >= 0.55)) results.Add(new SmartSearchResult(e, score, lex, sem));
        }
        return results.OrderByDescending(r => r.Score).Take(limit).ToList();
    }
}
