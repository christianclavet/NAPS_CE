using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAPS2.ImportExport.Pdf;
using NAPS2.Scan.Images.Transforms;

namespace NAPS2.Scan.Images
{
    public class ScannedImageRenderer
    {
        private readonly IPdfRenderer pdfRenderer;

        public ScannedImageRenderer(IPdfRenderer pdfRenderer)
        {
            this.pdfRenderer = pdfRenderer;
        }

        public async Task<Bitmap> Render(ScannedImage image, int outputSize = 0)
        {
            using (var snapshot = image.Preserve())
            {
                return await Render(snapshot, outputSize);
            }
        }

        public async Task<Bitmap> Render(ScannedImage.Snapshot snapshot, int outputSize = 0)
        {
            return await Task.Factory.StartNew(() => RenderNow(snapshot, outputSize));
        }

        /// <summary>
        /// Same as <see cref="Render(ScannedImage.Snapshot,int)"/>, but runs on the calling thread.
        /// Used by callers that already render on their own worker threads.
        /// </summary>
        public Bitmap RenderNow(ScannedImage.Snapshot snapshot, int outputSize = 0)
        {
            var path = snapshot.Source.RecoveryFilePath;
            Bitmap bitmap;
            if (snapshot.Source.FileFormat == null)
            {
                bitmap = pdfRenderer.Render(path).Single();
                if (outputSize > 0)
                {
                    bitmap = ShrinkBitmap(bitmap, outputSize);
                }
            }
            else
            {
                // Reduced size wanted (thumbnails): decode directly at that size when possible (much faster for JPEG)
                bitmap = outputSize > 0 ? ScaledImageDecoder.TryDecode(path, outputSize) : null;
                if (bitmap == null)
                {
                    // Loaded from memory: the page file is not kept locked while the bitmap is in use
                    bitmap = new Bitmap(new MemoryStream(File.ReadAllBytes(path)));
                    if (outputSize > 0)
                    {
                        bitmap = ShrinkBitmap(bitmap, outputSize);
                    }
                }
            }
            return Transform.PerformAll(bitmap, snapshot.TransformList);
        }

        private Bitmap ShrinkBitmap(Bitmap bitmap, int outputSize)
        {
            double scaleFactor = Math.Min(outputSize / (double)bitmap.Height, outputSize / (double)bitmap.Width);
            if (scaleFactor >= 1)
            {
                return bitmap;
            }
            var bitmap2 = new Bitmap((int)Math.Round(bitmap.Width * scaleFactor), (int)Math.Round(bitmap.Height * scaleFactor));
            using (var g = Graphics.FromImage(bitmap2))
            {
                // Bicubic is very slow on large pages; the prefiltered bilinear mode is as good when shrinking a lot
                g.InterpolationMode = scaleFactor < 0.5 ? InterpolationMode.HighQualityBilinear : InterpolationMode.HighQualityBicubic;
                g.DrawImage(bitmap, new Rectangle(Point.Empty, bitmap2.Size), new Rectangle(Point.Empty, bitmap.Size), GraphicsUnit.Pixel);
            }
            bitmap.Dispose();
            return bitmap2;
        }

        public async Task<Stream> RenderToStream(ScannedImage image)
        {
            using (var snapshot = image.Preserve())
            {
                return await RenderToStream(snapshot);
            }
        }

        public async Task<Stream> RenderToStream(ScannedImage.Snapshot snapshot)
        {
            using (var transformed = await Render(snapshot))
            {
                var stream = new MemoryStream();
                var format = transformed.PixelFormat == PixelFormat.Format1bppIndexed
                    ? ImageFormat.Png
                    : snapshot.Source.FileFormat ?? (snapshot.Source.RecoveryIndexImage.HighQuality ? ImageFormat.Png : ImageFormat.Jpeg);
                transformed.Save(stream, format);
                return stream;
            }
        }
    }
}
