// Window background image: load, desaturate, brighten, build a cached brush.
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SystemInfo;

/// <summary>
/// Builds a translucent ImageBrush from a custom background image.
/// Applies simple per-pixel saturation/brightness transforms directly (no WPF ColorMatrix dependency).
/// </summary>
public static class BackgroundImageService
{
    private static WriteableBitmap? _cached;
    private static string? _cachedPath;
    private static int _cachedSaturation;
    private static int _cachedBrightness;

    /// <summary>Transformed bitmap (or null when the image is missing/unreadable).</summary>
    public static WriteableBitmap? Transform(string path, int saturation, int brightness)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        int sat = Math.Clamp(saturation, 0, 100);
        int bright = Math.Clamp(brightness, 0, 100);
        if (_cachedPath == path && _cachedSaturation == sat && _cachedBrightness == bright && _cached is not null)
            return _cached;
        try
        {
            var source = new BitmapImage(new Uri(path, UriKind.Absolute));
            source.Freeze();
            var conv = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            if (w <= 0 || h <= 0) return null;
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];
            conv.CopyPixels(pixels, stride, 0);
            double satF = sat / 100.0, brightF = bright / 100.0;
            if (sat != 100 || bright != 100)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    double r = pixels[i], g = pixels[i + 1], b = pixels[i + 2];
                    double luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    double nr = (luma + (r - luma) * satF) * brightF;
                    double ng = (luma + (g - luma) * satF) * brightF;
                    double nb = (luma + (b - luma) * satF) * brightF;
                    pixels[i] = (byte)Math.Clamp((int)Math.Round(nr), 0, 255);
                    pixels[i + 1] = (byte)Math.Clamp((int)Math.Round(ng), 0, 255);
                    pixels[i + 2] = (byte)Math.Clamp((int)Math.Round(nb), 0, 255);
                }
            }
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
            wb.Freeze();
            _cachedPath = path;
            _cachedSaturation = sat;
            _cachedBrightness = bright;
            _cached = wb;
            return wb;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Ready-to-use ImageBrush. Caller owns opacity, stretch and alignment.</summary>
    public static ImageBrush? BuildBrush(string path, int saturation, int brightness)
        => Transform(path, saturation, brightness) is { } wb
            ? new ImageBrush(wb) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center }
            : null;

    /// <summary>Tiny preview brush for the settings dialog.</summary>
    public static ImageBrush? BuildPreviewBrush(string path, int saturation, int brightness, int maxWidth, int maxHeight)
    {
        var wb = Transform(path, saturation, brightness);
        if (wb is null) return null;
        return new ImageBrush(wb) { Stretch = Stretch.Uniform, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
    }
}
