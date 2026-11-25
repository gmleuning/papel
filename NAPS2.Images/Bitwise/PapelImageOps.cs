using NAPS2.Images.Transforms;

namespace NAPS2.Images.Bitwise;

/// <summary>
/// Image operations added by the Papel fork: adaptive thresholding, margin whitening and gutter detection.
/// Operates on plain byte buffers so it works with every image backend.
/// </summary>
public static class PapelImageOps
{
    internal static IMemoryImage AdaptiveThreshold(ImageContext imageContext, IMemoryImage image,
        AdaptiveThresholdTransform transform)
    {
        var gray = ReadGray(image, out int w, out int h);
        double scale = transform.OriginalWidth is > 0 ? w / (double) transform.OriginalWidth.Value : 1.0;

        if (transform.Flatten)
        {
            // Background estimate: a wide gaussian (sigma 30px at full size)
            var bg = GaussianBlur(gray, w, h, Math.Max(1.0, 30 * scale));
            for (int i = 0; i < gray.Length; i++)
            {
                int v = gray[i] * 210 / Math.Max((int) bg[i], 1);
                gray[i] = (byte) (v > 255 ? 255 : v);
            }
        }

        int block = Math.Max(3, (int) Math.Round(transform.BlockSize * scale)) | 1;
        // Same sigma OpenCV uses for ADAPTIVE_THRESH_GAUSSIAN_C
        double sigma = 0.3 * ((block - 1) * 0.5 - 1) + 0.8;
        var mean = GaussianBlur(gray, w, h, sigma);
        var output = new byte[gray.Length];
        for (int i = 0; i < gray.Length; i++)
        {
            output[i] = gray[i] > mean[i] - transform.C ? (byte) 255 : (byte) 0;
        }

        var result = imageContext.Create(w, h, ImagePixelFormat.Gray8);
        result.SetResolution(image.HorizontalResolution, image.VerticalResolution);
        result.OriginalFileFormat = image.OriginalFileFormat;
        WriteGray(result, output);

        var mono = result.CopyBlankWithPixelFormat(ImagePixelFormat.BW1);
        new CopyBitwiseImageOp().Perform(result, mono);
        result.Dispose();
        image.Dispose();
        return mono;
    }

    internal static unsafe IMemoryImage WhitenMargins(IMemoryImage image, MarginWhitenTransform transform)
    {
        int w = image.Width, h = image.Height;
        double xs = transform.OriginalWidth is > 0 ? w / (double) transform.OriginalWidth.Value : 1.0;
        double ys = transform.OriginalHeight is > 0 ? h / (double) transform.OriginalHeight.Value : 1.0;
        int left = Clamp((int) Math.Round(transform.Left * xs), 0, w);
        int right = Clamp((int) Math.Round(transform.Right * xs), 0, w);
        int top = Clamp((int) Math.Round(transform.Top * ys), 0, h);
        int bottom = Clamp((int) Math.Round(transform.Bottom * ys), 0, h);

        using var lockState = image.Lock(LockMode.ReadWrite, out var data);
        for (int y = 0; y < h; y++)
        {
            bool fullRow = y < top || y >= h - bottom;
            int physY = data.invertY ? h - 1 - y : y;
            byte* row = data.ptr + data.stride * physY;
            for (int x = 0; x < w; x++)
            {
                if (!fullRow && x >= left && x < w - right) continue;
                SetWhite(data, row, x);
            }
        }
        return image;
    }

    private static unsafe void SetWhite(BitwiseImageData data, byte* row, int x)
    {
        if (data.bitsPerPixel == 1)
        {
            byte mask = (byte) (0x80 >> (x & 7));
            bool whiteIsOne = !data.invertColorSpace;
            if (whiteIsOne) row[x >> 3] |= mask;
            else row[x >> 3] &= (byte) ~mask;
            return;
        }
        byte white = data.invertColorSpace ? (byte) 0 : (byte) 255;
        byte* p = row + x * data.bytesPerPixel;
        if (data.bytesPerPixel == 1)
        {
            *p = white;
            return;
        }
        p[data.rOff] = white;
        p[data.gOff] = white;
        p[data.bOff] = white;
        if (data.hasAlpha) p[data.aOff] = 255;
    }

    /// <summary>
    /// Finds the x position of the gutter on a two-page spread: the column in the middle 30% of the page that
    /// is either the darkest (a binding shadow) or the emptiest (a white gap between pages), whichever stands out
    /// more from its surroundings. Returns null if no clear gutter is found.
    /// </summary>
    public static int? FindGutter(IMemoryImage image)
    {
        var gray = ReadGray(image, out int w, out int h);
        if (w < 50 || h < 50) return null;
        // Column ink profile, skipping the outer 5% top/bottom where scanner edges live
        int y0 = h / 20, y1 = h - h / 20;
        var colMean = new double[w];
        for (int x = 0; x < w; x++)
        {
            long sum = 0;
            for (int y = y0; y < y1; y++) sum += gray[y * w + x];
            colMean[x] = sum / (double) (y1 - y0);
        }
        // Smooth over ~1% of the width
        int r = Math.Max(1, w / 100);
        var smooth = new double[w];
        for (int x = 0; x < w; x++)
        {
            double s = 0;
            int n = 0;
            for (int k = Math.Max(0, x - r); k <= Math.Min(w - 1, x + r); k++)
            {
                s += colMean[k];
                n++;
            }
            smooth[x] = s / n;
        }
        int lo = (int) (w * 0.35), hi = (int) (w * 0.65);
        double overall = smooth.Skip(lo).Take(hi - lo).Average();
        int darkX = lo, lightX = lo;
        for (int x = lo; x < hi; x++)
        {
            if (smooth[x] < smooth[darkX]) darkX = x;
            if (smooth[x] > smooth[lightX]) lightX = x;
        }
        double darkStrength = overall - smooth[darkX];
        double lightStrength = smooth[lightX] - overall;
        if (Math.Max(darkStrength, lightStrength) < 4)
        {
            // Nothing stands out; fall back to the exact centre
            return null;
        }
        return darkStrength >= lightStrength ? darkX : lightX;
    }

    private static unsafe byte[] ReadGray(IMemoryImage image, out int w, out int h)
    {
        using var gray = image.CopyWithPixelFormat(ImagePixelFormat.Gray8);
        w = gray.Width;
        h = gray.Height;
        var buf = new byte[w * h];
        using var lockState = gray.Lock(LockMode.ReadOnly, out var data);
        for (int y = 0; y < h; y++)
        {
            byte* row = data.ptr + data.stride * (data.invertY ? h - 1 - y : y);
            int o = y * w;
            for (int x = 0; x < w; x++)
            {
                byte* p = row + x * data.bytesPerPixel;
                int v = data.bytesPerPixel == 1
                    ? *p
                    : (p[data.rOff] * 299 + p[data.gOff] * 587 + p[data.bOff] * 114) / 1000;
                if (data.invertColorSpace) v = 255 - v;
                buf[o + x] = (byte) v;
            }
        }
        return buf;
    }

    private static unsafe void WriteGray(IMemoryImage image, byte[] buf)
    {
        int w = image.Width, h = image.Height;
        using var lockState = image.Lock(LockMode.WriteOnly, out var data);
        for (int y = 0; y < h; y++)
        {
            byte* row = data.ptr + data.stride * (data.invertY ? h - 1 - y : y);
            int o = y * w;
            for (int x = 0; x < w; x++)
            {
                byte v = buf[o + x];
                if (data.invertColorSpace) v = (byte) (255 - v);
                byte* p = row + x * data.bytesPerPixel;
                if (data.bytesPerPixel == 1)
                {
                    *p = v;
                }
                else
                {
                    p[data.rOff] = v;
                    p[data.gOff] = v;
                    p[data.bOff] = v;
                    if (data.hasAlpha) p[data.aOff] = 255;
                }
            }
        }
    }

    /// <summary>
    /// Approximates a gaussian blur with three box blurs (each O(n) regardless of radius).
    /// </summary>
    private static float[] GaussianBlur(byte[] src, int w, int h, double sigma)
    {
        var a = new float[src.Length];
        for (int i = 0; i < src.Length; i++) a[i] = src[i];
        var b = new float[src.Length];
        foreach (int radius in BoxRadii(sigma, 3))
        {
            BoxH(a, b, w, h, radius);
            BoxV(b, a, w, h, radius);
        }
        return a;
    }

    private static IEnumerable<int> BoxRadii(double sigma, int n)
    {
        double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
        int wl = (int) Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        int wu = wl + 2;
        double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4);
        int m = (int) Math.Round(mIdeal);
        for (int i = 0; i < n; i++)
        {
            yield return Math.Max(0, ((i < m ? wl : wu) - 1) / 2);
        }
    }

    private static void BoxH(float[] src, float[] dst, int w, int h, int r)
    {
        if (r == 0)
        {
            Array.Copy(src, dst, src.Length);
            return;
        }
        Parallel.For(0, h, y =>
        {
            int o = y * w;
            double sum = 0;
            // Edge pixels are extended (clamped) beyond the border
            for (int k = -r; k <= r; k++) sum += src[o + Clamp(k, 0, w - 1)];
            float inv = 1f / (2 * r + 1);
            for (int x = 0; x < w; x++)
            {
                dst[o + x] = (float) (sum * inv);
                sum += src[o + Math.Min(x + r + 1, w - 1)] - src[o + Math.Max(x - r, 0)];
            }
        });
    }

    private static void BoxV(float[] src, float[] dst, int w, int h, int r)
    {
        if (r == 0)
        {
            Array.Copy(src, dst, src.Length);
            return;
        }
        Parallel.For(0, w, x =>
        {
            double sum = 0;
            for (int k = -r; k <= r; k++) sum += src[Clamp(k, 0, h - 1) * w + x];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = (float) (sum * inv);
                sum += src[Math.Min(y + r + 1, h - 1) * w + x] - src[Math.Max(y - r, 0) * w + x];
            }
        });
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
}
