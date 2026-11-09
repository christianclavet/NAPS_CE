using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace NAPS2.Scan
{
    public static partial class BarcodeLocator
    {
        /// <summary>
        /// Extrait la boîte orientée vers une image à 0 degré : pour chaque pixel de sortie,
        /// on calcule la position source (rotation inverse) et on interpole bilinéairement.
        /// Hors de l'image source : blanc (prolonge la zone calme).
        /// </summary>
        private static unsafe Bitmap ExtractRectified(byte[] gray, int width, int height, Candidate c, double quietZone)
        {
            double outW = c.Length * (1 + 2 * quietZone);
            double outH = c.Height * (1 + 2 * quietZone);
            double step = 1.0; // pixels source par pixel de sortie
            double biggest = Math.Max(outW, outH);
            if (biggest > MaxOutputSide)
            {
                step = biggest / MaxOutputSide;
            }
            int ow = Math.Max(1, (int)Math.Ceiling(outW / step));
            int oh = Math.Max(1, (int)Math.Ceiling(outH / step));

            double ux = Math.Cos(c.Theta), uy = Math.Sin(c.Theta);
            double vx = -uy, vy = ux;
            double dxStep = step * ux;
            double dyStep = step * uy;
            double a0 = (0.5 - ow / 2.0) * step;
            double maxX = width - 1;
            double maxY = height - 1;

            var bitmap = new Bitmap(ow, oh, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte* scan0 = (byte*)data.Scan0;
                int stride = data.Stride;
                fixed (byte* g0 = gray)
                {
                    for (int oy = 0; oy < oh; oy++)
                    {
                        uint* dst = (uint*)(scan0 + (long)oy * stride);
                        double b = (oy + 0.5 - oh / 2.0) * step;
                        double px = c.CenterX + a0 * ux + b * vx - 0.5;
                        double py = c.CenterY + a0 * uy + b * vy - 0.5;

                        for (int ox = 0; ox < ow; ox++, px += dxStep, py += dyStep)
                        {
                            uint value;
                            if (px < 0 || py < 0 || px >= maxX || py >= maxY)
                            {
                                value = 255;
                            }
                            else
                            {
                                int x0 = (int)px;
                                int y0 = (int)py;
                                int fx = (int)((px - x0) * 256);
                                int fy = (int)((py - y0) * 256);
                                byte* p = g0 + (long)y0 * width + x0;
                                int top = p[0] * (256 - fx) + p[1] * fx;
                                int bottom = p[width] * (256 - fx) + p[width + 1] * fx;
                                value = (uint)((top * (256 - fy) + bottom * fy) >> 16);
                            }
                            dst[ox] = 0xFF000000u | (value << 16) | (value << 8) | value;
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }
    }
}
