using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace NAPS2.Scan
{
    public static partial class BarcodeLocator
    {
        private static unsafe void ToGray(Bitmap source, int width, int height, byte[] gray)
        {
            BitmapData data = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte* scan0 = (byte*)data.Scan0;
                int stride = data.Stride;
                fixed (byte* g0 = gray)
                {
                    for (int y = 0; y < height; y++)
                    {
                        byte* s = scan0 + (long)y * stride;
                        byte* d = g0 + (long)y * width;
                        for (int x = 0; x < width; x++, s += 4)
                        {
                            // BGRA : (R*77 + G*150 + B*29) / 256
                            d[x] = (byte)((s[2] * 77 + s[1] * 150 + s[0] * 29) >> 8);
                        }
                    }
                }
            }
            finally
            {
                source.UnlockBits(data);
            }
        }

        private static unsafe void Downsample(byte[] gray, int width, byte[] small, int sw, int sh, int factor)
        {
            int area = factor * factor;
            fixed (byte* g0 = gray)
            fixed (byte* s0 = small)
            {
                for (int y = 0; y < sh; y++)
                {
                    byte* outRow = s0 + (long)y * sw;
                    for (int x = 0; x < sw; x++)
                    {
                        if (factor == 1)
                        {
                            outRow[x] = g0[(long)y * width + x];
                            continue;
                        }
                        int sum = 0;
                        for (int dy = 0; dy < factor; dy++)
                        {
                            byte* p = g0 + (long)(y * factor + dy) * width + x * factor;
                            for (int dx = 0; dx < factor; dx++)
                            {
                                sum += p[dx];
                            }
                        }
                        outRow[x] = (byte)(sum / area);
                    }
                }
            }
        }

        /// <summary>
        /// Gradients centrés gx, gy accumulés par cellule : Jxx = sum gx², Jyy = sum gy², Jxy = sum gx*gy.
        /// Max par cellule : 64 * 2 * 255² (environ 8,3 M), donc tient dans un int.
        /// </summary>
        private static unsafe void AccumulateGradientTensor(byte[] small, int sw, int sh, int cols, int[] jxx, int[] jyy, int[] jxy)
        {
            fixed (byte* s0 = small)
            fixed (int* pxx = jxx)
            fixed (int* pyy = jyy)
            fixed (int* pxy = jxy)
            {
                for (int y = 1; y < sh - 1; y++)
                {
                    byte* row = s0 + (long)y * sw;
                    int cellRow = (y / CellSize) * cols;
                    for (int x = 1; x < sw - 1; x++)
                    {
                        int gx = row[x + 1] - row[x - 1];
                        int gy = row[x + sw] - row[x - sw];
                        if (gx * gx + gy * gy < MinGradientSq)
                        {
                            continue;
                        }
                        int cell = cellRow + x / CellSize;
                        pxx[cell] += gx * gx;
                        pyy[cell] += gy * gy;
                        pxy[cell] += gx * gy;
                    }
                }
            }
        }

        /// <summary>
        /// Dilatation ou érosion 1D (fenêtre 2r+1) sur un masque 0/1, en O(n) via sommes préfixes.
        /// Aux bords, la fenêtre est tronquée (l'érosion ne ronge donc pas les bords).
        /// </summary>
        private static void Morph1D(byte[] src, byte[] dst, int cols, int rows, int radius, bool horizontal, bool dilate, int[] prefix)
        {
            int lines = horizontal ? rows : cols;
            int length = horizontal ? cols : rows;
            int step = horizontal ? 1 : cols;
            int lineStep = horizontal ? cols : 1;

            for (int l = 0; l < lines; l++)
            {
                int start = l * lineStep;
                prefix[0] = 0;
                for (int i = 0; i < length; i++)
                {
                    prefix[i + 1] = prefix[i] + (src[start + i * step] != 0 ? 1 : 0);
                }
                for (int i = 0; i < length; i++)
                {
                    int lo = i - radius < 0 ? 0 : i - radius;
                    int hi = i + radius > length - 1 ? length - 1 : i + radius;
                    int sum = prefix[hi + 1] - prefix[lo];
                    bool on = dilate ? sum > 0 : sum == hi - lo + 1;
                    dst[start + i * step] = (byte)(on ? 1 : 0);
                }
            }
        }
    }
}
