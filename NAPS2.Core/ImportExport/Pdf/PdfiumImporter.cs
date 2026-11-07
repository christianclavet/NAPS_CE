using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.Config;
using NAPS2.Lang.Resources;
using NAPS2.Logging;
using NAPS2.Scan;
using NAPS2.Scan.Images;
using NAPS2.Util;
using PDFiumSharp;

namespace NAPS2.ImportExport.Pdf
{
    /// <summary>
    /// Imports PDF files by rendering every page with PDFium (replaces the PdfSharp/Ghostscript import path).
    /// </summary>
    public class PdfiumImporter : IPdfImporter
    {
        private readonly IErrorOutput errorOutput;
        private readonly IPdfPasswordProvider pdfPasswordProvider;
        private readonly ThumbnailRenderer thumbnailRenderer;
        private readonly IPdfRenderer pdfRenderer;
        private readonly UserConfigManager userConfigManager;

        public PdfiumImporter(IErrorOutput errorOutput, IPdfPasswordProvider pdfPasswordProvider, ThumbnailRenderer thumbnailRenderer, IPdfRenderer pdfRenderer, UserConfigManager userConfigManager)
        {
            this.errorOutput = errorOutput;
            this.pdfPasswordProvider = pdfPasswordProvider;
            this.thumbnailRenderer = thumbnailRenderer;
            this.pdfRenderer = pdfRenderer;
            this.userConfigManager = userConfigManager;
        }

        public ScannedImageSource Import(string filePath, ImportParams importParams, ProgressHandler progressCallback, CancellationToken cancelToken)
        {
            var source = new ScannedImageSource.Concrete();
            Task.Factory.StartNew(() => ImportPdf(filePath, importParams, progressCallback, cancelToken, source), TaskCreationOptions.LongRunning);
            return source;
        }

        private void ImportPdf(string filePath, ImportParams importParams, ProgressHandler progressCallback, CancellationToken cancelToken, ScannedImageSource.Concrete source)
        {
            bool aborted = false;
            try
            {
                pdfRenderer.ThrowIfCantRender();
                using (OpenedPdf opened = OpenWithPassword(filePath, out aborted))
                {
                    if (opened == null)
                    {
                        return;
                    }

                    int pageCount;
                    lock (PdfiumRenderer.Lock)
                    {
                        pageCount = opened.Document.Pages.Count;
                    }

                    int i = 0;
                    foreach (int pageIndex in importParams.Slice.Indices(pageCount).ToList())
                    {
                        if (cancelToken.IsCancellationRequested)
                        {
                            break;
                        }
                        progressCallback(i++, pageCount);

                        Bitmap bitmap;
                        lock (PdfiumRenderer.Lock)
                        {
                            bitmap = PdfiumRenderer.RenderPage(opened, pageIndex, userConfigManager.Config.VectorPdfDpi);
                        }
                        using (bitmap)
                        {
                            source.Put(CreateScannedImage(bitmap, filePath, importParams));
                        }
                    }
                }
            }
            catch (ImageRenderException e)
            {
                errorOutput.DisplayError(string.Format(MiscResources.ImportErrorNAPS2Pdf, Path.GetFileName(filePath)));
                Log.ErrorException("Error importing PDF file.", e);
            }
            catch (Exception e)
            {
                if (!aborted)
                {
                    errorOutput.DisplayError(string.Format(MiscResources.ImportErrorCouldNot, Path.GetFileName(filePath)));
                    Log.ErrorException("Error importing PDF file.", e);
                }
            }
            finally
            {
                source.Done();
            }
        }

        /// <summary>
        /// Opens the PDF, asking the password provider for a password each time PDFium rejects the file.
        /// Returns null and sets aborted if the user declines to provide a password.
        /// </summary>
        private OpenedPdf OpenWithPassword(string filePath, out bool aborted)
        {
            aborted = false;
            int passwordAttempts = 0;
            string password = null;
            while (true)
            {
                try
                {
                    return PdfiumRenderer.OpenPdf(filePath, password);
                }
                catch (PDFiumException)
                {
                    if (!pdfPasswordProvider.ProvidePassword(Path.GetFileName(filePath), passwordAttempts++, out password))
                    {
                        aborted = true;
                        return null;
                    }
                }
            }
        }

        private ScannedImage CreateScannedImage(Bitmap bitmap, string filePath, ImportParams importParams)
        {
            var image = new ScannedImage(bitmap, ScanBitDepth.C24Bit, true, -1);
            if (!importParams.NoThumbnails)
            {
                image.SetThumbnail(thumbnailRenderer.RenderThumbnail(bitmap));
            }
            if (importParams.DetectPatchCodes)
            {
                image.PatchCode = PatchCodeDetector.Detect(bitmap);
                if (image.PatchCode == PatchCode.Unknown)
                {
                    image.BarCodeData = PatchCodeDetector.DataBarcode;
                }
            }
            image.RecoveryIndexImage.SeparatorName = filePath;
            return image;
        }
    }
}
