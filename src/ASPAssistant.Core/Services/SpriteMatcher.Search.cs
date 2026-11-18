using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using OpenCvSharp;

namespace ASPAssistant.Core.Services;

public sealed partial class SpriteMatcher
{
    // 小搜索窗直接枚举位移。行前缀保存三个通道的一阶矩与合计平方和，
    // 透明区间不参与相关系数；乘积在连续的可见区间上批量累加。
    private sealed class SearchImage
    {
        public readonly int Width;
        public readonly short[] Pixels;
        public readonly Vector128<int>[] Prefix;

        public SearchImage(Mat image)
        {
            Width = image.Width;
            var bytes = new byte[Width * image.Height * 3];
            Marshal.Copy(image.Data, bytes, 0, bytes.Length);
            // 每段模板尾部补零，图像末尾也留出一组，最后一次向量读取可越过行尾。
            Pixels = new short[bytes.Length + Vector256<short>.Count];
            Prefix = new Vector128<int>[(Width + 1) * image.Height];
            for (int y = 0; y < image.Height; y++)
            {
                var sum = Vector128<int>.Zero;
                for (int x = 0; x < Width; x++)
                {
                    int i = (y * Width + x) * 3;
                    int b = Pixels[i] = (short)(bytes[i] - 128);
                    int g = Pixels[i + 1] = (short)(bytes[i + 1] - 128);
                    int r = Pixels[i + 2] = (short)(bytes[i + 2] - 128);
                    sum += Vector128.Create(b, g, r, b * b + g * g + r * r);
                    Prefix[y * (Width + 1) + x + 1] = sum;
                }
            }
        }
    }

    private sealed class SearchTemplate
    {
        private readonly record struct Run(int X, int Y, int Width, int Offset);
        private readonly Run[] _runs;
        private readonly short[] _color;
        private readonly double _meanB, _meanG, _meanR, _variance;
        public int Count { get; }

        public SearchTemplate(Mat image, Mat mask)
        {
            int w = image.Width, h = image.Height;
            var pixels = new byte[w * h * 3];
            var visible = new byte[w * h];
            Marshal.Copy(image.Data, pixels, 0, pixels.Length);
            Marshal.Copy(mask.Data, visible, 0, visible.Length);
            var runs = new List<Run>();
            var color = new List<short>();
            int b = 0, g = 0, r = 0;
            double squares = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w;)
                {
                    if (visible[y * w + x] == 0) { x++; continue; }
                    int start = x, offset = color.Count;
                    while (x < w && visible[y * w + x] != 0)
                    {
                        int i = (y * w + x++) * 3;
                        int cb = pixels[i] - 128, cg = pixels[i + 1] - 128, cr = pixels[i + 2] - 128;
                        color.Add((short)cb); color.Add((short)cg); color.Add((short)cr);
                        b += cb; g += cg; r += cr;
                        squares += cb * cb + cg * cg + cr * cr;
                    }
                    runs.Add(new(start, y, x - start, offset));
                    Count += x - start;
                    while (color.Count % Vector256<short>.Count != 0) color.Add(0);
                }
            _runs = runs.ToArray();
            _color = color.ToArray();
            _meanB = (double)b / Count; _meanG = (double)g / Count; _meanR = (double)r / Count;
            _variance = squares - b * _meanB - g * _meanG - r * _meanR;
        }

        public (Point, double) Best(SearchImage image, int left, int top, int cols, int rows)
        {
            double best = double.NegativeInfinity;
            var location = new Point();
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    var moments = Vector128<int>.Zero;
                    var products = Vector256<int>.Zero;
                    foreach (var run in _runs)
                    {
                        int row = top + y + run.Y, col = left + x + run.X;
                        int prefix = row * (image.Width + 1) + col;
                        moments += image.Prefix[prefix + run.Width] - image.Prefix[prefix];
                        int pos = (row * image.Width + col) * 3;
                        for (int i = 0; i < run.Width * 3; i += Vector256<short>.Count)
                        {
                            var a = Vector256.LoadUnsafe(ref _color[run.Offset + i]);
                            var b = Vector256.LoadUnsafe(ref image.Pixels[pos + i]);
                            products += Avx2.MultiplyAddAdjacent(a, b);
                        }
                    }
                    int dot = Vector256.Sum(products);
                    double bsum = moments[0], gsum = moments[1], rsum = moments[2];
                    double variance = moments[3] - (bsum * bsum + gsum * gsum + rsum * rsum) / Count;
                    double corr = (dot - bsum * _meanB - gsum * _meanG - rsum * _meanR) / Math.Sqrt(variance * _variance);
                    if (!double.IsFinite(corr)) corr = -1;
                    if (corr > best) { best = corr; location = new(x, y); }
                }
            return (location, best);
        }
    }
}
