using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using NAPS2.Config;
using NAPS2.Logging;
using NAPS2.Scan;
using NAPS2.Util;
using PDFiumSharp;
using PDFiumSharp.Types;

namespace NAPS2.ImportExport.Pdf
{
    /// <summary>
    /// Renders PDF pages to bitmaps with PDFium (replaces the Ghostscript renderer).
    /// Vector pages use the configured DPI; other pages are rendered at their native image resolution.
    /// </summary>
    public class PdfiumRenderer : IPdfRenderer
    {
        /// <summary>
        /// PDFium is not thread-safe. Every PDFium call made by the renderer and the importer goes through this lock.
        /// </summary>
        internal static readonly object Lock = new object();

        private readonly AppConfigManager appConfigManager;
        private readonly UserConfigManager userConfigManager;

        public PdfiumRenderer(AppConfigManager appConfigManager, UserConfigManager userConfigManager)
        {
            this.appConfigManager = appConfigManager;
            this.userConfigManager = userConfigManager;
        }

        public void ThrowIfCantRender()
        {
            if (appConfigManager.Config.DisableGenericPdfImport)
            {
                throw new ImageRenderException();
            }
        }

        public IEnumerable<Bitmap> Render(string path)
        {
            ThrowIfCantRender();

            using (var opened = OpenPdf(path, null))
            {
                int pageCount;
                lock (Lock)
                {
                    pageCount = opened.Document.Pages.Count;
                }
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    Bitmap bitmap;
                    lock (Lock)
                    {
                        bitmap = RenderPage(opened, pageIndex, userConfigManager.Config.VectorPdfDpi);
                    }
                    yield return bitmap;
                }
            }
        }

        /// <summary>
        /// Opens a PDF with PDFium. Throws PDFiumException if the file can't be loaded (e.g. wrong or missing password).
        /// </summary>
        internal static OpenedPdf OpenPdf(string path, string password)
        {
            byte[] data = File.ReadAllBytes(path);
            // PDFium keeps a pointer to the buffer for as long as the document is open, so the array must stay pinned
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                PdfDocument document;
                IntPtr nativeDocument;
                lock (Lock)
                {
                    document = new PdfDocument(data, 0, -1, password);
                    nativeDocument = PdfiumNative.LoadMemDocument(handle.AddrOfPinnedObject(), data.Length, password);
                }
                return new OpenedPdf(document, nativeDocument, data, handle);
            }
            catch
            {
                handle.Free();
                throw;
            }
        }

        /// <summary>
        /// Renders one page. Pages with vector content use <paramref name="vectorDpi"/>. Pages without vector content
        /// (image-only) are rendered at the native resolution of their image, so they are imported as-is.
        /// The caller must hold <see cref="Lock"/>.
        /// </summary>
        internal static Bitmap RenderPage(OpenedPdf opened, int pageIndex, int vectorDpi)
        {
            PdfPage page = opened.Document.Pages[pageIndex];
            int dpi = vectorDpi;
            IntPtr nativePage = IntPtr.Zero;
            try
            {
                nativePage = PdfiumNative.LoadPage(opened.NativeDocument, pageIndex);
                if (nativePage != IntPtr.Zero && !PdfiumPageAnalyzer.HasVectorContent(nativePage))
                {
                    int nativeDpi = PdfiumPageAnalyzer.NativeImageDpi(nativePage, page.Width, page.Height);
                    if (nativeDpi > 0)
                    {
                        dpi = nativeDpi;
                    }
                }
            }
            catch (EntryPointNotFoundException e)
            {
                // The bundled PDFium binaries are too old to expose page object functions.
                // Fall back to the configured DPI for every page instead of failing the import.
                Log.ErrorException("PDFium library lacks page object functions; using the vector DPI for all pages.", e);
            }
            finally
            {
                if (nativePage != IntPtr.Zero)
                {
                    PdfiumNative.ClosePage(nativePage);
                }
            }
            return RenderPage(page, dpi);
        }

        /// <summary>
        /// Renders a page at the given DPI. The caller must hold <see cref="Lock"/>.
        /// </summary>
        internal static Bitmap RenderPage(PdfPage page, int dpi)
        {
            // Page sizes are expressed in points (1/72 inch)
            double widthInches = page.Width / 72.0;
            double heightInches = page.Height / 72.0;

            // Cap the resolution to 10k pixels in each dimension
            int pageDpi = Math.Min(dpi, (int)Math.Min(10000 / heightInches, 10000 / widthInches));
            int width = Math.Max(1, (int)Math.Round(widthInches * pageDpi));
            int height = Math.Max(1, (int)Math.Round(heightInches * pageDpi));

            using (var pdfBitmap = new PDFiumBitmap(width, height, false))
            {
                // Pages are drawn on a white background, as they would be when printed
                pdfBitmap.FillRectangle(0, 0, width, height, new FPDF_COLOR(255, 255, 255));
                page.Render(pdfBitmap);

                // Copy into managed memory, because the native buffer is released when pdfBitmap is disposed
                using (var wrapper = new Bitmap(width, height, pdfBitmap.Stride, PixelFormat.Format32bppRgb, pdfBitmap.Scan0))
                {
                    Bitmap copy = wrapper.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format32bppRgb);
                    copy.SafeSetResolution(pageDpi, pageDpi);
                    return copy;
                }
            }
        }
    }

    /// <summary>
    /// A PDFium document along with the pinned buffer it was loaded from.
    /// </summary>
    internal sealed class OpenedPdf : IDisposable
    {
        private readonly GCHandle handle;
        private readonly byte[] data;

        public OpenedPdf(PdfDocument document, IntPtr nativeDocument, byte[] data, GCHandle handle)
        {
            Document = document;
            NativeDocument = nativeDocument;
            this.data = data;
            this.handle = handle;
        }

        /// <summary>The managed PDFiumSharp document, used for rendering.</summary>
        public PdfDocument Document { get; }

        /// <summary>A second, raw PDFium handle to the same bytes, used to inspect page objects.</summary>
        public IntPtr NativeDocument { get; }

        public void Dispose()
        {
            lock (PdfiumRenderer.Lock)
            {
                if (NativeDocument != IntPtr.Zero)
                {
                    PdfiumNative.CloseDocument(NativeDocument);
                }
                Document.Close();
            }
            handle.Free();
        }
    }
}
