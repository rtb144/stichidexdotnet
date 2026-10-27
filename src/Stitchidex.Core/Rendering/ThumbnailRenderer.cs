using SkiaSharp;
using Stitchidex.Core.Models;
namespace Stitchidex.Core.Rendering;

public static class ThumbnailRenderer
{
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
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, size / 200f), StrokeCap = SKStrokeCap.Round };
            StitchPoint? prev = null;
            foreach (var p in pts)
            {
                if (p.Command == EmbroideryCommand.Stitch && prev is { } q && q.Command == EmbroideryCommand.Stitch && q.ColorIndex == p.ColorIndex)
                {
                    var c = pattern.Colors.Count > 0 ? pattern.Colors[p.ColorIndex % pattern.Colors.Count] : ThreadColor.FromPecIndex(20);
                    paint.Color = new SKColor(c.R, c.G, c.B);
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
