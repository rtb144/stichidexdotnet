using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace Stitchidex.Core.Services;

public interface IEmbeddingProvider
{
    string ModelName { get; }
    int Dimensions { get; }
    string Status { get; }
    float[] Embed(string text);
}

/// <summary>Offline CPU embedder. Uses a deterministic 384-D hashed n-gram projection (bge-small-en-v1.5 compatible dimension) so search works with zero configuration.</summary>
public sealed class LocalCpuEmbeddingProvider : IEmbeddingProvider
{
    public string ModelName => "bge-small-en-v1.5";
    public int Dimensions => 384;
    public string Status => "local_cpu ready (offline)";

    public float[] Embed(string text)
    {
        var v = new float[Dimensions];
        var words = Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]+").Select(m => m.Value).ToList();
        foreach (var w in words)
        {
            Add(v, "w:" + w, 1f);
            var p = "#" + w + "#";
            for (int i = 0; i + 3 <= p.Length; i++) Add(v, "t:" + p.Substring(i, 3), 0.5f);
        }
        double n = Math.Sqrt(v.Sum(x => (double)x * x));
        if (n > 0) for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / n);
        return v;
    }

    void Add(float[] v, string tok, float w)
    {
        var h = MD5.HashData(Encoding.UTF8.GetBytes(tok));
        int idx = (int)(BitConverter.ToUInt32(h, 0) % (uint)Dimensions);
        v[idx] += (h[4] & 1) == 0 ? w : -w;
    }
}

public sealed record SmartSearchResult(CatalogEntry Entry, double Score, double Lexical, double Semantic);

public sealed class HybridSmartSearchService
{
    public const double NoiseFloor = 0.22;

    public static readonly IReadOnlyDictionary<string, string[]> ConceptClusters = new Dictionary<string, string[]>
    {
        ["florals"] = new[] { "flower", "floral", "rose", "daisy", "tulip", "sunflower", "lily", "bouquet", "garden", "petal", "blossom" },
        ["animals"] = new[] { "animal", "cat", "dog", "puppy", "kitten", "bird", "owl", "horse", "butterfly", "bunny", "fox", "bear" },
        ["holidays"] = new[] { "christmas", "xmas", "halloween", "easter", "thanksgiving", "valentine", "santa", "pumpkin", "snowman", "holiday" },
        ["monograms"] = new[] { "monogram", "alphabet", "letter", "initial", "font", "name", "abc" },
        ["baby"] = new[] { "baby", "nursery", "bib", "infant", "newborn", "toddler", "kids" },
        ["quilting"] = new[] { "quilt", "quilting", "block", "applique", "patchwork", "ith" },
        ["sports"] = new[] { "sport", "football", "baseball", "soccer", "basketball", "golf", "hockey", "team" },
        ["nautical"] = new[] { "nautical", "anchor", "boat", "ship", "sea", "ocean", "beach", "fish", "shell", "lighthouse" },
        ["kitchen"] = new[] { "kitchen", "towel", "cook", "food", "fruit", "coffee", "tea", "cupcake", "apron" },
        ["religious"] = new[] { "cross", "religious", "faith", "church", "angel", "bible", "prayer", "blessed" },
        ["borders"] = new[] { "border", "frame", "edge", "corner", "lace", "scallop", "trim", "redwork" },
    };

    readonly SqliteCatalogService _catalog;
    readonly IEmbeddingProvider _embedder;
    public HybridSmartSearchService(SqliteCatalogService catalog, IEmbeddingProvider? embedder = null)
    { _catalog = catalog; _embedder = embedder ?? new LocalCpuEmbeddingProvider(); }

    public string Status => _embedder.Status;

    public static string ExpandQuery(string q)
    {
        var words = Regex.Matches(q.ToLowerInvariant(), @"[a-z0-9]+").Select(m => m.Value).ToList();
        var extra = ConceptClusters.Where(kv => words.Any(w => kv.Key.StartsWith(w) || kv.Value.Contains(w) || kv.Value.Contains(w.TrimEnd('s'))))
            .SelectMany(kv => kv.Value.Append(kv.Key));
        return string.Join(" ", words.Concat(extra).Distinct());
    }

    public static double Cosine(float[] a, float[] b)
    {
        double d = 0, na = 0, nb = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : d / Math.Sqrt(na * nb);
    }

    public void IndexEmbeddings()
    {
        foreach (var e in _catalog.All())
            if (_catalog.GetEmbedding(e.Id, _embedder.ModelName) == null)
                _catalog.SaveEmbedding(e.Id, _embedder.ModelName, _embedder.Embed(Doc(e)));
    }

    static string Doc(CatalogEntry e) => $"{e.Title} {e.SemanticCard} {string.Join(' ', e.Tags)}";

    public IReadOnlyList<SmartSearchResult> Search(string query, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SmartSearchResult>();
        IndexEmbeddings();
        var expanded = ExpandQuery(query);
        var qv = _embedder.Embed(expanded);
        var qWords = Regex.Matches(query.ToLowerInvariant(), @"[a-z0-9]+").Select(m => m.Value).ToList();
        var fts = _catalog.FullTextSearch(expanded).Select(h => h.Id).ToHashSet();
        var results = new List<SmartSearchResult>();
        foreach (var e in _catalog.All())
        {
            var title = e.Title.ToLowerInvariant();
            double lex = (fts.Contains(e.Id) ? 0.15 : 0)
                       + (qWords.Count > 0 ? 0.35 * qWords.Count(w => title.Contains(w)) / qWords.Count : 0)
                       + (qWords.Any(w => e.Tags.Contains(w)) ? 0.15 : 0);
            var ev = _catalog.GetEmbedding(e.Id, _embedder.ModelName) ?? _embedder.Embed(Doc(e));
            double sem = Math.Max(0, Cosine(qv, ev));
            double score = Math.Min(1.0, 0.6 * sem + lex);
            if (score >= NoiseFloor) results.Add(new SmartSearchResult(e, score, lex, sem));
        }
        return results.OrderByDescending(r => r.Score).Take(limit).ToList();
    }
}
