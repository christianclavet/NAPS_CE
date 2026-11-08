using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using NAPS2.ImportExport.Images;
using NAPS2.ImportExport.Pdf;
using NAPS2.Ocr;
using NAPS2.Scan;
using NAPS2.Scan.Batch;
using NAPS2.Scan.Images;

namespace NAPS2.Config
{
    public class PinnedServer
    {
        public string Address { get; set; }

        public string Thumbprint { get; set; }
    }

    public class UserConfig
    {
        public const int CURRENT_VERSION = 2;

        public const int DEFAULT_VECTOR_PDF_DPI = 300;

        public const int MIN_VECTOR_PDF_DPI = 72;

        public const int MAX_VECTOR_PDF_DPI = 1200;

        public int Version { get; set; }

        public string Culture { get; set; }

        public List<FormState> FormStates { get; set; } = new List<FormState>();

        public HashSet<string> BackgroundOperations { get; set; } = new HashSet<string>();

        public DateTime? FirstRunDate { get; set; }

        public DateTime? LastDonatePromptDate { get; set; }

        public bool EnableOcr { get; set; }

        public string OcrLanguageCode { get; set; }

        public OcrMode OcrMode { get; set; }

        public bool? OcrAfterScanning { get; set; }

        public string LastImageExt { get; set; }

        public PdfSettings PdfSettings { get; set; }

        public ImageSettings ImageSettings { get; set; }

        public int ThumbnailSize { get; set; } = ThumbnailRenderer.DEFAULT_SIZE;

        public bool Quickview { get; set; } = true; //true will hide the Quickview per default

        public bool IndexWindow { get; set; } = true; //This will display or hide the index text window editor

        public int Splitter1_distance { get; set; } = 590; // Default value for the view splitter

        public bool DarkMode { get; set; }

        public BatchSettings LastBatchSettings { get; set; }

        public DockStyle DesktopToolStripDock { get; set; }

        public KeyboardShortcuts KeyboardShortcuts { get; set; }

        public List<NamedPageSize> CustomPageSizePresets { get; set; } = new List<NamedPageSize>();

        public List<ScanProxyConfig> SavedProxies { get; set; } = new List<ScanProxyConfig>();

        // Certificate thumbprints of scanning servers this application has already trusted.
        // A list is used because XmlSerializer cannot serialize a Dictionary.
        public List<PinnedServer> PinnedServers { get; set; } = new List<PinnedServer>();

        public string project { get; set; }

        public string LastPath {  get; set; }

        public int VectorPdfDpi { get; set; } = DEFAULT_VECTOR_PDF_DPI;

        // Persisted fill color of the mask tool (ARGB), white by default
        public int MaskColorArgb { get; set; } = System.Drawing.Color.White.ToArgb();

        // Détection de codes-barres : filtre de format. Vide = aucun filtre (tous les codes sont acceptés).
        public bool BarcodeFilterEnabled { get; set; }

        // Expression régulière que doit respecter le texte d'un code-barres lu (exemple : ^SDOC\d{9}$)
        public string BarcodeFilterPattern { get; set; } = "";

    }
}
