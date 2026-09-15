using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DKImageSimpleUpscaler;

internal enum ResizeMethod
{
    Lanczos3,
    Bicubic,
    NearestNeighbor
}

internal static class ImageProcessor
{
    internal static Bitmap Resize(Bitmap source, int width, int height, ResizeMethod method)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException();
        return method == ResizeMethod.Lanczos3
            ? ResizeLanczos(source, width, height)
            : ResizeGdi(source, width, height, method);
    }

    private static Bitmap ResizeGdi(Bitmap source, int width, int height, ResizeMethod method)
    {
        var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        dest.SetResolution(source.HorizontalResolution > 0 ? source.HorizontalResolution : 96,
                           source.VerticalResolution > 0 ? source.VerticalResolution : 96);

        using var g = Graphics.FromImage(dest);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.PixelOffsetMode = method == ResizeMethod.NearestNeighbor ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
        g.InterpolationMode = method == ResizeMethod.NearestNeighbor
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        return dest;
    }

    private sealed record Contribution(int[] Indices, double[] Weights);

    private static Bitmap ResizeLanczos(Bitmap input, int dstWidth, int dstHeight)
    {
        using var source = ToArgb32(input);
        int srcWidth = source.Width;
        int srcHeight = source.Height;
        byte[] src = ReadBytes(source, out int srcStride);

        var xContrib = BuildContributions(srcWidth, dstWidth, 3.0);
        var yContrib = BuildContributions(srcHeight, dstHeight, 3.0);

        var temp = new float[dstWidth * srcHeight * 4];
        Parallel.For(0, srcHeight, y =>
        {
            for (int x = 0; x < dstWidth; x++)
            {
                var c = xContrib[x];
                double b = 0, g = 0, r = 0, a = 0;
                for (int k = 0; k < c.Indices.Length; k++)
                {
                    int sx = c.Indices[k];
                    double w = c.Weights[k];
                    int p = y * srcStride + sx * 4;
                    b += src[p] * w;
                    g += src[p + 1] * w;
                    r += src[p + 2] * w;
                    a += src[p + 3] * w;
                }

                int t = (y * dstWidth + x) * 4;
                temp[t] = (float)b;
                temp[t + 1] = (float)g;
                temp[t + 2] = (float)r;
                temp[t + 3] = (float)a;
            }
        });

        var dest = new Bitmap(dstWidth, dstHeight, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, dstWidth, dstHeight);
        var data = dest.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int dstStride = data.Stride;
            var output = new byte[Math.Abs(dstStride) * dstHeight];
            Parallel.For(0, dstHeight, y =>
            {
                var c = yContrib[y];
                for (int x = 0; x < dstWidth; x++)
                {
                    double b = 0, g = 0, r = 0, a = 0;
                    for (int k = 0; k < c.Indices.Length; k++)
                    {
                        int sy = c.Indices[k];
                        double w = c.Weights[k];
                        int t = (sy * dstWidth + x) * 4;
                        b += temp[t] * w;
                        g += temp[t + 1] * w;
                        r += temp[t + 2] * w;
                        a += temp[t + 3] * w;
                    }

                    int p = y * dstStride + x * 4;
                    output[p] = ClampByte(b);
                    output[p + 1] = ClampByte(g);
                    output[p + 2] = ClampByte(r);
                    output[p + 3] = ClampByte(a);
                }
            });
            Marshal.Copy(output, 0, data.Scan0, output.Length);
        }
        finally
        {
            dest.UnlockBits(data);
        }

        dest.SetResolution(input.HorizontalResolution > 0 ? input.HorizontalResolution : 96,
                           input.VerticalResolution > 0 ? input.VerticalResolution : 96);
        return dest;
    }

    private static Contribution[] BuildContributions(int srcSize, int dstSize, double radius)
    {
        double scale = dstSize / (double)srcSize;
        double filterScale = scale < 1.0 ? scale : 1.0;
        double support = radius / filterScale;
        var result = new Contribution[dstSize];

        for (int d = 0; d < dstSize; d++)
        {
            double center = (d + 0.5) / scale - 0.5;
            int left = (int)Math.Ceiling(center - support);
            int right = (int)Math.Floor(center + support);

            var indices = new List<int>();
            var weights = new List<double>();
            double sum = 0;

            for (int s = left; s <= right; s++)
            {
                double distance = (center - s) * filterScale;
                double weight = Lanczos(distance, radius) * filterScale;
                if (Math.Abs(weight) < 1e-12) continue;

                int clamped = Math.Clamp(s, 0, srcSize - 1);
                indices.Add(clamped);
                weights.Add(weight);
                sum += weight;
            }

            if (Math.Abs(sum) < 1e-12)
            {
                indices.Clear();
                weights.Clear();
                indices.Add(Math.Clamp((int)Math.Round(center), 0, srcSize - 1));
                weights.Add(1.0);
            }
            else
            {
                for (int i = 0; i < weights.Count; i++) weights[i] /= sum;
            }

            result[d] = new Contribution(indices.ToArray(), weights.ToArray());
        }
        return result;
    }

    private static double Lanczos(double x, double a)
    {
        x = Math.Abs(x);
        if (x < 1e-12) return 1.0;
        if (x >= a) return 0.0;
        double pix = Math.PI * x;
        return (Math.Sin(pix) / pix) * (Math.Sin(pix / a) / (pix / a));
    }

    internal static void SharpenInPlace(Bitmap bitmap, int strengthPercent)
    {
        if (strengthPercent <= 0) return;
        double amount = Math.Clamp(strengthPercent, 0, 100) / 100.0 * 1.25;

        using var src = (Bitmap)bitmap.Clone();
        byte[] input = ReadBytes(src, out int stride);
        int width = src.Width;
        int height = src.Height;

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var output = new byte[Math.Abs(data.Stride) * height];
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int pOut = y * data.Stride + x * 4;
                    int pCenter = y * stride + x * 4;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        double blur = 0;
                        int weightSum = 0;
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int sy = Math.Clamp(y + oy, 0, height - 1);
                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int sx = Math.Clamp(x + ox, 0, width - 1);
                                int w = (ox == 0 && oy == 0) ? 4 : (ox == 0 || oy == 0 ? 2 : 1);
                                blur += input[sy * stride + sx * 4 + ch] * w;
                                weightSum += w;
                            }
                        }
                        blur /= weightSum;
                        double value = input[pCenter + ch] + amount * (input[pCenter + ch] - blur);
                        output[pOut + ch] = ClampByte(value);
                    }
                    output[pOut + 3] = input[pCenter + 3];
                }
            });
            Marshal.Copy(output, 0, data.Scan0, output.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap ToArgb32(Bitmap source)
    {
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(copy);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(source, 0, 0);
        return copy;
    }

    private static byte[] ReadBytes(Bitmap bitmap, out int stride)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            stride = data.Stride;
            var bytes = new byte[Math.Abs(stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    internal static Bitmap Blend(Bitmap safeImage, Bitmap aiImage, int aiStrengthPercent)
    {
        if (safeImage.Width != aiImage.Width || safeImage.Height != aiImage.Height)
            throw new ArgumentException("혼합할 이미지 크기가 다릅니다.");

        double aiWeight = Math.Clamp(aiStrengthPercent, 0, 100) / 100.0;
        if (aiWeight <= 0) return (Bitmap)safeImage.Clone();
        if (aiWeight >= 1) return (Bitmap)aiImage.Clone();

        using var safe = ToArgb32(safeImage);
        using var ai = ToArgb32(aiImage);
        byte[] safeBytes = ReadBytes(safe, out int safeStride);
        byte[] aiBytes = ReadBytes(ai, out int aiStride);

        var output = new Bitmap(safe.Width, safe.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, output.Width, output.Height);
        var data = output.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * output.Height];
            double safeWeight = 1.0 - aiWeight;
            Parallel.For(0, output.Height, y =>
            {
                for (int x = 0; x < output.Width; x++)
                {
                    int ps = y * safeStride + x * 4;
                    int pa = y * aiStride + x * 4;
                    int po = y * data.Stride + x * 4;
                    for (int ch = 0; ch < 4; ch++)
                        bytes[po + ch] = ClampByte(safeBytes[ps + ch] * safeWeight + aiBytes[pa + ch] * aiWeight);
                }
            });
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            output.UnlockBits(data);
        }

        output.SetResolution(safeImage.HorizontalResolution > 0 ? safeImage.HorizontalResolution : 96,
                             safeImage.VerticalResolution > 0 ? safeImage.VerticalResolution : 96);
        return output;
    }

    private static byte ClampByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
