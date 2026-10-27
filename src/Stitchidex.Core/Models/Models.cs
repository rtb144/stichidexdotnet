using System.Globalization;
namespace Stitchidex.Core.Models;

public enum EmbroideryCommand { Move, Stitch, Jump, Trim, ColorChange, Stop, End }

/// <summary>Absolute stitch position in 0.1 mm units.</summary>
public readonly record struct StitchPoint(double X, double Y, EmbroideryCommand Command, int ColorIndex);

public sealed record ThreadColor(byte R, byte G, byte B, string Name)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public static ThreadColor FromHex(string hex, string? name = null)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6) throw new FormatException($"Invalid hex color '{hex}'");
        var v = int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new ThreadColor((byte)(v >> 16), (byte)(v >> 8), (byte)v, name ?? "#" + h.ToUpperInvariant());
    }

    /// <summary>Brother PEC palette; index 0 = Unknown, 1..64 real threads.</summary>
    public static readonly IReadOnlyList<ThreadColor> BrotherPecPalette = new[]
    {
        ("000000","Unknown"),("1a0a94","Prussian Blue"),("0f75ff","Blue"),("00934c","Teal Green"),("babdfe","Corn Flower Blue"),
        ("ec0000","Red"),("e4995a","Reddish Brown"),("cc48ab","Magenta"),("fdc4fa","Light Lilac"),("dd84cd","Lilac"),
        ("6bd38a","Mint Green"),("e4a945","Deep Gold"),("ffbd42","Orange"),("ffe600","Yellow"),("6cd900","Lime Green"),
        ("c1a941","Brass"),("b5ad97","Silver"),("ba9c5f","Russet Brown"),("faf59e","Cream Brown"),("808080","Pewter"),
        ("000000","Black"),("001cdf","Ultramarine"),("df00b8","Royal Purple"),("626262","Dark Gray"),("69260d","Dark Brown"),
        ("ff0060","Deep Rose"),("bf8200","Light Brown"),("f39178","Salmon Pink"),("ff6805","Vermilion"),("f0f0f0","White"),
        ("c832cd","Violet"),("b0bf9b","Seacrest"),("65bfeb","Sky Blue"),("ffba04","Pumpkin"),("fff06c","Cream Yellow"),
        ("feca15","Khaki"),("f38101","Clay Brown"),("37a923","Leaf Green"),("23465f","Peacock Blue"),("a6a695","Gray"),
        ("cebfa6","Warm Gray"),("96aa02","Dark Olive"),("ffe3c6","Linen"),("ff99d7","Pink"),("007004","Deep Green"),
        ("edccfb","Lavender"),("c089d8","Wisteria Violet"),("e7d9b4","Beige"),("e90e86","Carmine"),("cf6829","Amber Red"),
        ("408615","Olive Green"),("db1797","Dark Fuchsia"),("ffa704","Tangerine"),("b9ffff","Light Blue"),("228927","Emerald Green"),
        ("b612cd","Purple"),("00aa00","Moss Green"),("fea9dc","Flesh Pink"),("fed510","Harvest Gold"),("0097df","Electric Blue"),
        ("ffff84","Lemon Yellow"),("cfe774","Fresh Green"),("ffc864","Applique Material"),("ffc8c8","Applique Position"),("ffc8c8","Applique"),
    }.Select(t => FromHex(t.Item1, t.Item2)).ToArray();

    /// <summary>Janome JEF palette (subset of common indices, 0..26 wraps).</summary>
    public static readonly IReadOnlyList<ThreadColor> JanomePalette = new[]
    {
        ("000000","Placeholder"),("000000","Black"),("ffffff","White"),("ffff17","Sunflower"),("fa9f5d","Hazel"),
        ("5cba5e","Green Dust"),("4a6be6","Blue"),("c1458c","Purple"),("e02d58","Light Red"),("c7c7c7","Silver Gray"),
        ("f7d7a3","Light Amber"),("ff5f2d","Orange"),("29a336","Leaf Green"),("2e62a1","Sea Blue"),("f2b2c5","Pink"),
        ("cfacd3","Lavender"),("b15e26","Saddle Brown"),("ae0c3b","Wine Red"),("80d2e0","Light Blue"),("e8e04f","Light Yellow"),
        ("1a1a5c","Navy Blue"),("7d7d7d","Gray"),("fec000","Gold"),("4f1f82","Violet"),("ffc0ff","Light Pink"),("027900","Dark Green"),("ed1c24","Red"),
    }.Select(t => FromHex(t.Item1, t.Item2)).ToArray();

    public static ThreadColor FromPecIndex(int i) => BrotherPecPalette[(i >= 0 && i < BrotherPecPalette.Count) ? i : 0];
    public static ThreadColor FromJanomeIndex(int i) => JanomePalette[(i > 0 && i < JanomePalette.Count) ? i : 1];
    public static ThreadColor? FindByName(string name) =>
        BrotherPecPalette.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class EmbroideryPattern
{
    public static readonly (string Label, double W, double H)[] StandardHoops =
    {
        ("100x100mm (4x4 in)",100,100),("130x180mm (5x7 in)",130,180),("160x260mm (6x10 in)",160,260),
        ("200x200mm (8x8 in)",200,200),("200x300mm (8x12 in)",200,300),("240x360mm (9.5x14 in)",240,360),
    };

    public string Title { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FormatFamily { get; set; } = "";
    public List<StitchPoint> Stitches { get; } = new();
    public List<ThreadColor> Colors { get; } = new();
    public int StitchCount { get; set; }
    public int ColorChangeCount { get; set; }
    public int JumpCount { get; set; }
    public int TrimCount { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public string HoopCategory { get; set; } = "Unknown";
    public byte[]? PngThumbnail { get; set; }
    public string SemanticCard { get; set; } = "";
    public List<string> Tags { get; } = new();

    public static string ClassifyHoop(double w, double h)
    {
        if (w <= 0 && h <= 0) return "Unknown";
        double a = Math.Min(w, h), b = Math.Max(w, h);
        foreach (var (label, hw, hh) in StandardHoops)
            if (a <= Math.Min(hw, hh) && b <= Math.Max(hw, hh)) return label;
        return "Oversize (> 240x360mm)";
    }

    public void ComputeStats()
    {
        StitchCount = Stitches.Count(s => s.Command == EmbroideryCommand.Stitch);
        ColorChangeCount = Stitches.Count(s => s.Command == EmbroideryCommand.ColorChange);
        JumpCount = Stitches.Count(s => s.Command == EmbroideryCommand.Jump);
        TrimCount = Stitches.Count(s => s.Command == EmbroideryCommand.Trim);
        var pts = Stitches.Where(s => s.Command == EmbroideryCommand.Stitch).ToList();
        if (pts.Count > 0)
        {
            WidthMm = Math.Round((pts.Max(p => p.X) - pts.Min(p => p.X)) / 10.0, 1);
            HeightMm = Math.Round((pts.Max(p => p.Y) - pts.Min(p => p.Y)) / 10.0, 1);
        }
        HoopCategory = ClassifyHoop(WidthMm, HeightMm);
        SemanticCard = $"{Title} | {FormatFamily} | {StitchCount} stitches | {Colors.Count} colors " +
                       $"({string.Join(", ", Colors.Select(c => c.Name).Distinct())}) | {WidthMm}x{HeightMm} mm | hoop {HoopCategory}" +
                       (Tags.Count > 0 ? " | tags: " + string.Join(", ", Tags) : "");
    }
}
