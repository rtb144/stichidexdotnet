using System.Text.RegularExpressions;
namespace Stitchidex.Core.Search;

public sealed record SpellSuggestion(string? CorrectedQuery, IReadOnlyList<string> Suggestions);

/// <summary>Typo-tolerant search helper: Damerau-Levenshtein (transpositions cost 1) + prefix completion
/// over the embroidery concept vocabulary plus words from the user's own catalog.</summary>
public static class SearchSpellChecker
{
    static readonly Regex WordRx = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
    public static readonly HashSet<string> BaseVocabulary =
        SearchText.Groups.SelectMany(g => g).Where(w => w.Length >= 3).ToHashSet();

    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    public static int MaxDistance(string w) => w.Length >= 5 ? 2 : w.Length == 4 ? 1 : 0;

    /// <summary>Best correction for a misspelled word, or null when the word is known / no close match.</summary>
    public static string? Correct(string word, ISet<string> vocab)
    {
        word = word.ToLowerInvariant();
        if (vocab.Contains(word) || vocab.Contains(word.TrimEnd('s')) || word.Any(char.IsDigit)) return null;
        int max = MaxDistance(word);
        if (max == 0) return null;
        return vocab.Where(v => Math.Abs(v.Length - word.Length) <= max)
            .Select(v => (v, d: Distance(word, v))).Where(t => t.d <= max)
            .OrderBy(t => t.d).ThenBy(t => t.v[0] == word[0] ? 0 : 1).ThenBy(t => Math.Abs(t.v.Length - word.Length)).ThenBy(t => t.v)
            .Select(t => t.v).FirstOrDefault();
    }

    public static HashSet<string> BuildVocabulary(IEnumerable<string>? catalogWords)
    {
        var v = new HashSet<string>(BaseVocabulary);
        if (catalogWords != null) foreach (var w in catalogWords) if (w.Length >= 3 && !w.Any(char.IsDigit)) v.Add(w.ToLowerInvariant());
        return v;
    }

    public static SpellSuggestion SuggestCorrections(string query, IEnumerable<string>? catalogWords = null, int maxSuggestions = 6)
        => SuggestCorrections(query, BuildVocabulary(catalogWords), maxSuggestions);

    public static SpellSuggestion SuggestCorrections(string query, ISet<string> vocab, int maxSuggestions = 6)
    {
        var words = WordRx.Matches((query ?? "").ToLowerInvariant()).Select(m => m.Value).ToList();
        if (words.Count == 0) return new SpellSuggestion(null, Array.Empty<string>());
        bool changed = false;
        var fixedWords = words.Select(w => { var c = Correct(w, vocab); if (c != null) { changed = true; return c; } return w; }).ToList();
        var corrected = changed ? string.Join(' ', fixedWords) : null;
        var last = words[^1];
        var sugg = new List<string>();
        if (corrected != null) sugg.Add(corrected);
        if (last.Length >= 3)
        {
            var head = string.Join(' ', fixedWords.Take(fixedWords.Count - 1));
            foreach (var v in vocab.Where(v => v.StartsWith(last) && v != last).OrderBy(v => v.Length).ThenBy(v => v).Take(maxSuggestions))
                sugg.Add((head + " " + v).Trim());
        }
        return new SpellSuggestion(corrected, sugg.Distinct().Take(maxSuggestions).ToList());
    }
}
