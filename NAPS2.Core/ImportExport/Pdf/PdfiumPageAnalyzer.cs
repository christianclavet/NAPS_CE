using System;

namespace NAPS2.ImportExport.Pdf
{
    /// <summary>
    /// Decides, for a page, whether it should be rasterized at the configured vector DPI or imported as-is.
    /// </summary>
    internal static class PdfiumPageAnalyzer
    {
        /// <summary>
        /// Returns true if the page contains vector content (paths or shadings), in which case it is rasterized
        /// at the configured DPI. Pages made only of images (or text) are not vector pages and keep their native resolution.
        /// </summary>
        public static bool HasVectorContent(IntPtr page)
        {
            int count = PdfiumNative.CountObjects(page);
            for (int i = 0; i < count; i++)
            {
                IntPtr pageObject = PdfiumNative.GetObject(page, i);
                int type = PdfiumNative.GetObjectType(pageObject);
                // Form XObjects are not inspected: they may contain vectors, but are not classified as vector pages here
                if (type == PdfiumNative.PAGEOBJ_PATH || type == PdfiumNative.PAGEOBJ_SHADING)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the highest pixel density (DPI) of the page's images, or 0 if the page has no image with a known size.
        /// Used so that image-only pages can be imported at their own native resolution.
        /// </summary>
        public static int NativeImageDpi(IntPtr page, double pageWidthPoints, double pageHeightPoints)
        {
            if (pageWidthPoints <= 0 || pageHeightPoints <= 0)
            {
                return 0;
            }
            int best = 0;
            int count = PdfiumNative.CountObjects(page);
            for (int i = 0; i < count; i++)
            {
                IntPtr pageObject = PdfiumNative.GetObject(page, i);
                if (PdfiumNative.GetObjectType(pageObject) != PdfiumNative.PAGEOBJ_IMAGE)
                {
                    continue;
                }
                if (!PdfiumNative.GetImagePixelSize(pageObject, out uint pixelWidth, out uint pixelHeight))
                {
                    continue;
                }
                int dpiX = (int)Math.Round(pixelWidth / (pageWidthPoints / 72.0));
                int dpiY = (int)Math.Round(pixelHeight / (pageHeightPoints / 72.0));
                best = Math.Max(best, Math.Max(dpiX, dpiY));
            }
            return best;
        }
    }
}
