using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Stitchidex.Core.Search;
namespace Stitchidex.Core.Services;

public interface IEmbeddingProvider
{
    string ModelName { get; }
    int Dimensions { get; }
    string Status { get; }
    float[] Embed(string text);
}

/// <summary>Offline 768-D concept + compound-word + n-gram embedder (always available, zero download).</summary>
public sealed class LocalCpuEmbeddingProvider : IEmbeddingProvider
{
    public string ModelName => "concept-ngram-768-v2";
    public int Dimensions => 768;
    public string Status => "local concept embedder";

    public float[] Embed(string text)
    {
        var v = new float[Dimensions];
        var toks = SearchText.SplitCompoundTokens(text);
        foreach (var w in toks)
        {
            Add(v, "w:" + w, 1f);
            var p = "#" + w + "#";
            for (int i = 0; i + 3 <= p.Length; i++) Add(v, "t:" + p.Substring(i, 3), 0.3f);
        }
        foreach (var c in SearchText.Concepts(toks))
            for (int k = 0; k < 4; k++) Add(v, $"concept:{c}:{k}", 1.5f);
        Normalize(v);
        return v;
    }

    internal static void Normalize(float[] v)
    {
        double n = Math.Sqrt(v.Sum(x => (double)x * x));
        if (n > 0) for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / n);
    }

    void Add(float[] v, string tok, float w)
    {
        var h = MD5.HashData(Encoding.UTF8.GetBytes(tok));
        int idx = (int)(BitConverter.ToUInt32(h, 0) % (uint)Dimensions);
        v[idx] += (h[4] & 1) == 0 ? w : -w;
    }
}

/// <summary>Minimal BERT uncased WordPiece tokenizer.</summary>
public sealed class WordPieceTokenizer
{
    readonly Dictionary<string, int> _vocab;
    public int Cls, Sep, Unk, Pad;
    public WordPieceTokenizer(string vocabPath)
    {
        _vocab = File.ReadLines(vocabPath).Select((l, i) => (l, i)).ToDictionary(t => t.l, t => t.i);
        Cls = _vocab["[CLS]"]; Sep = _vocab["[SEP]"]; Unk = _vocab["[UNK]"]; Pad = _vocab["[PAD]"];
    }
    public List<int> Encode(string text, int max = 128)
    {
        var ids = new List<int> { Cls };
        var norm = new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        foreach (Match m in Regex.Matches(norm, @"\w+|[^\w\s]"))
        {
            var w = m.Value; int start = 0; var sub = new List<int>();
            while (start < w.Length)
            {
                int end = w.Length, found = -1;
                while (end > start)
                {
                    var piece = (start > 0 ? "##" : "") + w[start..end];
                    if (_vocab.TryGetValue(piece, out var id)) { found = id; break; }
                    end--;
                }
                if (found < 0) { sub = new List<int> { Unk }; break; }
                sub.Add(found); start = end;
            }
            ids.AddRange(sub);
            if (ids.Count >= max - 1) break;
        }
        if (ids.Count > max - 1) ids = ids.Take(max - 1).ToList();
        ids.Add(Sep);
        return ids;
    }
}

/// <summary>BAAI/bge-base-en-v1.5 (768-D, 109M params, int8 ONNX) via OnnxRuntime. CLS pooling + L2 norm. Thread-safe.</summary>
public sealed class OnnxBgeEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    public const string ModelId = "bge-base-en-v1.5";
    public const string ModelUrl = "https://huggingface.co/Xenova/bge-base-en-v1.5/resolve/main/onnx/model_quantized.onnx";
    public const string VocabUrl = "https://huggingface.co/Xenova/bge-base-en-v1.5/resolve/main/vocab.txt";
    readonly InferenceSession _session;
    readonly WordPieceTokenizer _tok;
    readonly bool _needsTypeIds;
    public string ModelName => ModelId + "-onnx";
    public int Dimensions => 768;
    public string Status => "bge-base-en-v1.5 (ONNX, CPU)";

    public static string DefaultModelDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stitchidex", "models", ModelId);

    public static bool IsAvailable(string? dir = null)
    {
        dir ??= DefaultModelDir();
        return File.Exists(Path.Combine(dir, "model_quantized.onnx")) && File.Exists(Path.Combine(dir, "vocab.txt"));
    }

    public OnnxBgeEmbeddingProvider(string? dir = null)
    {
        dir ??= DefaultModelDir();
        var o = new SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2), GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _session = new InferenceSession(Path.Combine(dir, "model_quantized.onnx"), o);
        _tok = new WordPieceTokenizer(Path.Combine(dir, "vocab.txt"));
        _needsTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");
    }

    /// <summary>Downloads the model into the cache dir if missing (call from a background thread).</summary>
    public static async Task<bool> EnsureDownloadedAsync(string? dir = null, CancellationToken ct = default)
    {
        dir ??= DefaultModelDir();
        if (IsAvailable(dir)) return true;
        try
        {
            Directory.CreateDirectory(dir);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            foreach (var (url, name) in new[] { (VocabUrl, "vocab.txt"), (ModelUrl, "model_quantized.onnx") })
            {
                var dest = Path.Combine(dir, name); if (File.Exists(dest)) continue;
                var tmp = dest + ".part";
                await using (var s = await http.GetStreamAsync(url, ct)) await using (var f = File.Create(tmp)) await s.CopyToAsync(f, ct);
                File.Move(tmp, dest, true);
            }
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    public float[] Embed(string text)
    {
        var ids = _tok.Encode(text);
        int n = ids.Count;
        var inputIds = new DenseTensor<long>(ids.Select(i => (long)i).ToArray(), new[] { 1, n });
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, n).ToArray(), new[] { 1, n });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", inputIds), NamedOnnxValue.CreateFromTensor("attention_mask", mask) };
        if (_needsTypeIds) inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[n], new[] { 1, n })));
        using var res = _session.Run(inputs);
        var h = res.First().AsTensor<float>();
        var v = new float[768];
        for (int i = 0; i < 768; i++) v[i] = h[0, 0, i]; // CLS pooling
        LocalCpuEmbeddingProvider.Normalize(v);
        return v;
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>Neural (bge-base) ⊕ concept embedder, weighted concat so cosine = w·neural + (1-w)·concept.</summary>
public sealed class BlendedEmbeddingProvider : IEmbeddingProvider
{
    readonly IEmbeddingProvider _neural, _concept;
    readonly float _a, _b;
    public BlendedEmbeddingProvider(IEmbeddingProvider neural, IEmbeddingProvider concept, double neuralWeight = 0.65)
    { _neural = neural; _concept = concept; _a = (float)Math.Sqrt(neuralWeight); _b = (float)Math.Sqrt(1 - neuralWeight); }
    public string ModelName => _neural.ModelName + "+" + _concept.ModelName;
    public int Dimensions => _neural.Dimensions + _concept.Dimensions;
    public string Status => _neural.Status + " + concepts";
    public float[] Embed(string text)
    {
        var a = _neural.Embed(text); var b = _concept.Embed(text);
        var v = new float[a.Length + b.Length];
        for (int i = 0; i < a.Length; i++) v[i] = a[i] * _a;
        for (int i = 0; i < b.Length; i++) v[a.Length + i] = b[i] * _b;
        return v;
    }

    /// <summary>Best available: blended bge-base if cached, else concept embedder.</summary>
    public static IEmbeddingProvider CreateDefault(string? dir = null)
    {
        var local = new LocalCpuEmbeddingProvider();
        if (!OnnxBgeEmbeddingProvider.IsAvailable(dir)) return local;
        try { return new BlendedEmbeddingProvider(new OnnxBgeEmbeddingProvider(dir), local); }
        catch (Exception) { return local; }
    }
}
