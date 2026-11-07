using System;
using System.Runtime.InteropServices;

namespace NAPS2.ImportExport.Pdf
{
    /// <summary>
    /// Raw PDFium calls used to inspect the objects of a page. PDFiumSharp's managed API doesn't expose page objects.
    /// The native library is pdfium_x64.dll or pdfium_x86.dll, depending on the process bitness.
    /// </summary>
    internal static class PdfiumNative
    {
        // Values of FPDF_PAGEOBJ_* from fpdf_edit.h
        public const int PAGEOBJ_TEXT = 1;
        public const int PAGEOBJ_PATH = 2;
        public const int PAGEOBJ_IMAGE = 3;
        public const int PAGEOBJ_SHADING = 4;
        public const int PAGEOBJ_FORM = 5;

        private static bool Is64Bit => IntPtr.Size == 8;

        public static IntPtr LoadMemDocument(IntPtr data, int size, string password) =>
            Is64Bit ? Native64.FPDF_LoadMemDocument(data, size, password) : Native86.FPDF_LoadMemDocument(data, size, password);

        public static void CloseDocument(IntPtr document)
        {
            if (Is64Bit) Native64.FPDF_CloseDocument(document); else Native86.FPDF_CloseDocument(document);
        }

        public static IntPtr LoadPage(IntPtr document, int pageIndex) =>
            Is64Bit ? Native64.FPDF_LoadPage(document, pageIndex) : Native86.FPDF_LoadPage(document, pageIndex);

        public static void ClosePage(IntPtr page)
        {
            if (Is64Bit) Native64.FPDF_ClosePage(page); else Native86.FPDF_ClosePage(page);
        }

        public static int CountObjects(IntPtr page) =>
            Is64Bit ? Native64.FPDFPage_CountObjects(page) : Native86.FPDFPage_CountObjects(page);

        public static IntPtr GetObject(IntPtr page, int index) =>
            Is64Bit ? Native64.FPDFPage_GetObject(page, index) : Native86.FPDFPage_GetObject(page, index);

        public static int GetObjectType(IntPtr pageObject) =>
            Is64Bit ? Native64.FPDFPageObj_GetType(pageObject) : Native86.FPDFPageObj_GetType(pageObject);

        public static bool GetImagePixelSize(IntPtr imageObject, out uint width, out uint height)
        {
            int ok = Is64Bit
                ? Native64.FPDFImageObj_GetImagePixelSize(imageObject, out width, out height)
                : Native86.FPDFImageObj_GetImagePixelSize(imageObject, out width, out height);
            return ok != 0;
        }
    }

    internal static class Native64
    {
        [DllImport("pdfium_x64.dll", EntryPoint = "FPDF_LoadMemDocument")]
        public static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, string password);

        [DllImport("pdfium_x64.dll", EntryPoint = "FPDF_CloseDocument")]
        public static extern void FPDF_CloseDocument(IntPtr document);

        [DllImport("pdfium_x64.dll", EntryPoint = "FPDF_LoadPage")]
        public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

        [DllImport("pdfium_x64.dll", EntryPoint = "FPDF_ClosePage")]
        public static extern void FPDF_ClosePage(IntPtr page);

        [DllImport("pdfium_x64.dll", EntryPoint = "FPDFPage_CountObjects")]
        public static extern int FPDFPage_CountObjects(IntPtr page);

        [DllImport("pdfium_x64.dll", EntryPoint = "FPDFPage_GetObject")]
        public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);

        [DllImport("pdfium_x64.dll", EntryPoint = "FPDFPageObj_GetType")]
        public static extern int FPDFPageObj_GetType(IntPtr pageObject);

        [DllImport("pdfium_x64.dll", EntryPoint = "FPDFImageObj_GetImagePixelSize")]
        public static extern int FPDFImageObj_GetImagePixelSize(IntPtr imageObject, out uint width, out uint height);
    }

    internal static class Native86
    {
        [DllImport("pdfium_x86.dll", EntryPoint = "FPDF_LoadMemDocument")]
        public static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, string password);

        [DllImport("pdfium_x86.dll", EntryPoint = "FPDF_CloseDocument")]
        public static extern void FPDF_CloseDocument(IntPtr document);

        [DllImport("pdfium_x86.dll", EntryPoint = "FPDF_LoadPage")]
        public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

        [DllImport("pdfium_x86.dll", EntryPoint = "FPDF_ClosePage")]
        public static extern void FPDF_ClosePage(IntPtr page);

        [DllImport("pdfium_x86.dll", EntryPoint = "FPDFPage_CountObjects")]
        public static extern int FPDFPage_CountObjects(IntPtr page);

        [DllImport("pdfium_x86.dll", EntryPoint = "FPDFPage_GetObject")]
        public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);

        [DllImport("pdfium_x86.dll", EntryPoint = "FPDFPageObj_GetType")]
        public static extern int FPDFPageObj_GetType(IntPtr pageObject);

        [DllImport("pdfium_x86.dll", EntryPoint = "FPDFImageObj_GetImagePixelSize")]
        public static extern int FPDFImageObj_GetImagePixelSize(IntPtr imageObject, out uint width, out uint height);
    }
}
