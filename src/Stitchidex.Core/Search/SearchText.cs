using System.Text;
using System.Text.RegularExpressions;
namespace Stitchidex.Core.Search;

/// <summary>Search-text utilities: embroidery concept groups, compound-word splitting, folder/PEC context cards.</summary>
public static class SearchText
{
    public static readonly string[] ConceptGroups =
    {
        "winter christmas xmas holiday noel santa claus reindeer rudolph antler sleigh sled ski elk deer snowman snow frosty snowflake flake icicle ice frozen holly mistletoe wreath ornament stocking elf gingerbread nativity bell candy cane festive yule penguin polar mitten mittens scarf igloo cocoa",
        "construction underconstruction build builder building site workzone equipment heavy machinery machine vehicle truck digger excavator trackhoe backhoe loader bulldozer dozer bulldoze crane dump tractor grader bobcat caterpillar forklift cement mixer cone hardhat roller skid steer",
        "halloween spooky ghost pumpkin jackolantern lantern witch bat spider skull skeleton zombie monster vampire haunted autumn fall harvest thanksgiving turkey leaves scarecrow",
        "easter spring bunny rabbit egg chick basket pastel lamb risen carrot",
        "floral flower rose daisy tulip sunflower lily hibiscus orchid botanical bloom blossom petal leaf vine garden bouquet",
        "animal pet dog puppy paw bone cat kitten horse pony cow pig sheep goat chicken rooster barn farm wildlife bear fox wolf owl bird eagle duck butterfly bee ladybug frog",
        "ocean sea nautical beach anchor boat ship sail fish shark whale dolphin turtle shell seashell lighthouse mermaid crab starfish wave summer",
        "baby infant newborn nursery onesie bib pacifier rattle stroller toddler child kids boy girl cute princess prince teddy",
        "kitchen cook baking bake chef apron towel coffee tea cup mug wine fruit vegetable berry apple cake cupcake cookie pie pizza bbq food drink",
        "sport sports football soccer baseball basketball golf tennis hockey volleyball team ball fishing hunting camping outdoors tent hiking mountain",
        "love heart valentine romance wedding bride groom anniversary mom mother dad father grandma nana grandpa family home",
        "religious faith cross angel church bible prayer blessed jesus god",
        "monogram alphabet letter initial font number frame crest shield applique patch badge keyfob fob snap tab ith",
        "border quilt quilting block patchwork lace scallop trim redwork corner edge",
        "patriotic america usa flag eagle star stripe military army navy veteran soldier freedom july",
        "fantasy unicorn dragon fairy castle space rocket planet alien astronaut dinosaur dino rex",
        "music guitar note piano art craft sewing embroidery stitch thread scissors tools hammer wrench tool profession nurse doctor teacher firefighter police",
    };
    public static readonly HashSet<string>[] Groups = ConceptGroups.Select(g => g.Split(' ').ToHashSet()).ToArray();
    static readonly HashSet<string> Vocab = Groups.SelectMany(g => g).Where(w => w.Length >= 3)
        .Concat(new[] { "under", "construction", "track", "hoe", "back", "snow", "man", "flake", "bull", "dozer", "fork", "lift", "ginger", "bread", "sun", "flower", "butter", "fly", "jack", "lantern", "dump", "truck", "hard", "hat", "work", "zone", "rein", "deer", "lady", "bug", "star", "fish", "sea", "shell", "light", "house", "tree", "candy", "cane", "ice", "cycle", "key", "fob", "cup", "cake", "sled" })
        .ToHashSet();
    static readonly HashSet<string> GenericDirs = new(StringComparer.OrdinalIgnoreCase)
    { "home", "users", "user", "downloads", "desktop", "documents", "embroidery", "patterns", "pattern", "samples", "workspace", "designs", "files", "tmp", "mnt", "media", "box", "root", "c", "d", "pes", "dst", "jef", "exp", "vp3", "hus", "xxx" };

    static IEnumerable<string> Words(string s) =>
        Regex.Matches(Regex.Replace(s ?? "", @"([a-z])([A-Z])", "$1 $2").ToLowerInvariant(), @"[a-z0-9]+").Select(m => m.Value);

    /// <summary>Splits camelCase, separators and glued words (underconstructio → under construction).</summary>
    public static List<string> SplitCompoundTokens(string text)
    {
        var o = new List<string>();
        var words = Words(text).ToList();
        for (int j = 0; j + 1 < words.Count; j++) // "track hoe" -> trackhoe, "snow man" -> snowman
        {
            var joined = words[j] + words[j + 1];
            if (Groups.Any(g => g.Contains(joined))) o.Add(joined);
        }
        foreach (var w in words)
        {
            o.Add(w);
            if (w.Length < 6 || w.Any(char.IsDigit)) continue;
            for (int i = 3; i <= w.Length - 3; i++)
            {
                string a = w[..i];
                if (!Vocab.Contains(a)) continue;
                for (int k = 1; k <= 2 && i + k <= w.Length - 3; k++) // jack-o-lantern
                    if (Vocab.Contains(w[(i + k)..])) { o.Add(a); o.Add(w[(i + k)..]); goto next; }
                string b = w[i..];
                string? bm = Vocab.Contains(b) ? b : Vocab.FirstOrDefault(v => v.Length >= 6 && v.StartsWith(b) && b.Length >= v.Length - 2);
                if (Vocab.Contains(a) && bm != null) { o.Add(a); o.Add(bm); if (bm != b) o.Add(a + bm); break; }
            }
            next:;
        }
        return o.Distinct().ToList();
    }

    public static IEnumerable<string> FolderContext(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        return dir.Split('/', '\\').Where(s => s.Length > 0 && !GenericDirs.Contains(s)).TakeLast(2);
    }

    /// <summary>Concept-group indexes touched by any token.</summary>
    public static IEnumerable<int> Concepts(IEnumerable<string> tokens)
    {
        var t = tokens.SelectMany(x => new[] { x, x.TrimEnd('s') }).ToHashSet();
        for (int i = 0; i < Groups.Length; i++) if (Groups[i].Overlaps(t)) yield return i;
    }

    /// <summary>Clean semantic text (no stitch/hoop boilerplate): title + folders + PEC label + split words + tags + notes.</summary>
    public static string BuildCard(string title, string path, IEnumerable<string> labels, IEnumerable<string> tags, string notes = "")
    {
        var parts = new List<string> { title };
        parts.AddRange(FolderContext(path));
        parts.AddRange(labels);
        parts.AddRange(tags);
        if (!string.IsNullOrWhiteSpace(notes)) parts.Add(notes);
        var toks = SplitCompoundTokens(string.Join(' ', parts));
        return string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct()) + " || " + string.Join(' ', toks);
    }

    /// <summary>Reads the 16-char PEC "LA:" label from a .pes/.pec file without full parsing.</summary>
    public static string? ReadPecLabel(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            using var fs = File.OpenRead(path);
            var buf = new byte[Math.Min(fs.Length, 1 << 20)]; fs.ReadExactly(buf);
            int pec = ext == ".pec" ? 8 : ext == ".pes" && buf.Length > 12 ? BitConverter.ToInt32(buf, 8) : -1;
            if (pec < 0 || pec + 19 > buf.Length || Encoding.ASCII.GetString(buf, pec, 3) != "LA:") return null;
            var l = Encoding.ASCII.GetString(buf, pec + 3, 16).Trim(' ', '\0');
            return l.Length > 0 ? l : null;
        }
        catch (Exception) { return null; }
    }
}
