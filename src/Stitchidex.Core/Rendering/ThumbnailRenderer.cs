using SkiaSharp;
using Stitchidex.Core.Models;
namespace Stitchidex.Core.Rendering;

public static class ThumbnailRenderer
{
    /// <summary>Lift near-black threads to a readable slate/silver so they show on the #2D3748 preview canvas.</summary>
    internal static (byte, byte, byte) Visible(ThreadColor c)
    {
        double lum = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        if (lum >= 40) return (c.R, c.G, c.B);
        return ((byte)Math.Min(255, c.R + 120), (byte)Math.Min(255, c.G + 130), (byte)Math.Min(255, c.B + 145));
    }

    public static byte[] RenderThumbnailPng(EmbroideryPattern pattern, int size = 256, int padding = 8)
    {
        using var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        var pts = pattern.Stitches;
        var st = pts.Where(p => p.Command == EmbroideryCommand.Stitch).ToList();
        if (st.Count > 0)
        {
            double minX = st.Min(p => p.X), maxX = st.Max(p => p.X), minY = st.Min(p => p.Y), maxY = st.Max(p => p.Y);
            double w = Math.Max(maxX - minX, 1), h = Math.Max(maxY - minY, 1);
            double scale = (size - 2.0 * padding) / Math.Max(w, h);
            double ox = padding + ((size - 2.0 * padding) - w * scale) / 2, oy = padding + ((size - 2.0 * padding) - h * scale) / 2;
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.6f, size / 140f), StrokeCap = SKStrokeCap.Round };
            using var dot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            StitchPoint? prev = null;
            foreach (var p in pts)
            {
                if (p.Command == EmbroideryCommand.Stitch && st.Count < 2000)
                {
                    var cd = pattern.Colors.Count > 0 ? pattern.Colors[p.ColorIndex % pattern.Colors.Count] : ThreadColor.FromPecIndex(20);
                    var (dr, dg, db) = Visible(cd);
                    dot.Color = new SKColor(dr, dg, db);
                    canvas.DrawCircle((float)(ox + (p.X - minX) * scale), (float)(oy + (p.Y - minY) * scale), Math.Max(1.6f, size / 90f), dot);
                }
                if (p.Command == EmbroideryCommand.Stitch && prev is { } q && q.Command == EmbroideryCommand.Stitch && q.ColorIndex == p.ColorIndex)
                {
                    var c = pattern.Colors.Count > 0 ? pattern.Colors[p.ColorIndex % pattern.Colors.Count] : ThreadColor.FromPecIndex(20);
                    var (r, g, bl) = Visible(c);
                    paint.Color = new SKColor(r, g, bl);
                    canvas.DrawLine((float)(ox + (q.X - minX) * scale), (float)(oy + (q.Y - minY) * scale),
                                    (float)(ox + (p.X - minX) * scale), (float)(oy + (p.Y - minY) * scale), paint);
                }
                prev = p;
            }
        }
        canvas.Flush();
        using var img = SKImage.FromBitmap(bmp);
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }
}
