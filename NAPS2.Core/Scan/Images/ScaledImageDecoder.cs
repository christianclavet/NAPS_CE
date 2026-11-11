using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAPS2.Logging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace NAPS2.Scan.Images
{
    /// <summary>
    /// Decodes an image file directly at a reduced size with WIC (Windows Imaging Component).
    /// For JPEG files the decoder scales while decoding (1/2, 1/4, 1/8), so the full-size pixels are never
    /// produced: much faster and much less memory than decoding the whole page and shrinking it afterwards.
    /// When WIC is not available (e.g. Mono), it is disabled and callers use the normal GDI+ decoding.
    /// </summary>
    internal static class ScaledImageDecoder
    {
        private static volatile bool disabled;

        /// <summary>
        /// Returns the image reduced to fit within maxSize x maxSize, or null when the image is already small
        /// enough or cannot be decoded this way (the caller then decodes it normally).
        /// The result is a 32bpp ARGB bitmap at 96 DPI, like the GDI+ shrinking it replaces.
        /// </summary>
        public static Bitmap TryDecode(string path, int maxSize)
        {
            if (disabled || maxSize <= 0)
            {
                return null;
            }
            try
            {
                return Decode(path, maxSize);
            }
            catch (Exception e) when (IsWicMissing(e))
            {
                disabled = true;
                Log.ErrorException("WIC is not available, thumbnails are decoded with GDI+", e);
                return null;
            }
            catch (Exception e)
            {
                Log.ErrorException("Error decoding a reduced image, using the normal decoding", e);
                return null;
            }
        }

        private static bool IsWicMissing(Exception e)
        {
            if (e is TypeLoadException || e is MissingMethodException)
            {
                return true;
            }
            var fileName = (e as FileNotFoundException)?.FileName ?? (e as FileLoadException)?.FileName;
            return fileName != null && (fileName.StartsWith("PresentationCore", StringComparison.OrdinalIgnoreCase)
                                        || fileName.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase));
        }

        // Kept separate (and not inlined) so a missing WPF assembly fails here, where it is caught
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Bitmap Decode(string path, int maxSize)
        {
            // The bytes are read once: no file lock is kept, and the header and the pixels come from memory
            var bytes = File.ReadAllBytes(path);

            int width, height;
            using (var headerStream = new MemoryStream(bytes))
            {
                var decoder = BitmapDecoder.Create(headerStream,
                    BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }

            double scale = Math.Min(maxSize / (double)width, maxSize / (double)height);
            if (scale >= 1)
            {
                return null;
            }
            int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(height * scale));

            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = targetWidth;
                image.DecodePixelHeight = targetHeight;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();

            BitmapSource source = image.Format == PixelFormats.Bgra32
                ? (BitmapSource)image
                : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

            var result = new Bitmap(source.PixelWidth, source.PixelHeight, PixelFormat.Format32bppArgb);
            var data = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                source.CopyPixels(Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride);
            }
            catch
            {
                result.UnlockBits(data);
                result.Dispose();
                throw;
            }
            result.UnlockBits(data);
            return result;
        }
    }
}
