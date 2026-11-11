#region Usings

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Runtime.InteropServices;

using NAPS2.Config;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Pdf;
using NAPS2.Lang;
using NAPS2.Lang.Resources;
using NAPS2.Logging;
using NAPS2.Ocr;
using NAPS2.Operation;
using NAPS2.Platform;
using NAPS2.Recovery;
using NAPS2.Scan;
using NAPS2.Scan.Exceptions;
using NAPS2.Scan.Images;
using NAPS2.Scan.Wia;
using NAPS2.Scan.Wia.Native;
using NAPS2.Util;
using NAPS2.Worker;
using ZXing;
using NAPS2.ImportExport.Images;
using Castle.Core.Internal;
using static System.Net.Mime.MediaTypeNames;

#endregion

namespace NAPS2.WinForms
{
    public partial class FDesktop : FormBase
    {
        #region Dependencies

        private static readonly MethodInfo ToolStripPanelSetStyle = typeof(ToolStripPanel).GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly StringWrapper stringWrapper;
        private readonly AppConfigManager appConfigManager;
        private readonly RecoveryManager recoveryManager;
        private readonly OcrManager ocrManager;
        private readonly IProfileManager profileManager;
        private readonly IScanPerformer scanPerformer;
        private readonly IScannedImagePrinter scannedImagePrinter;
        private readonly ChangeTracker changeTracker;
        private readonly StillImage stillImage;
        private readonly IOperationFactory operationFactory;
        private readonly IUserConfigManager userConfigManager;
        private readonly KeyboardShortcutManager ksm;
        private readonly ThumbnailRenderer thumbnailRenderer;
        private readonly WinFormsExportHelper exportHelper;
        private readonly ScannedImageRenderer scannedImageRenderer;
        private readonly ScannedImageHelper scannedImageHelper;
        private readonly NotificationManager notify;
        private readonly CultureInitializer cultureInitializer;
        private readonly IWorkerServiceFactory workerServiceFactory;
        private readonly IOperationProgress operationProgress;
        private readonly ImageSettingsContainer imageSettingsContainer;

        #endregion

        #region State Fields

        private readonly ScannedImageList imageList = new ScannedImageList();
        private readonly AutoResetEvent renderThumbnailsWaitHandle = new AutoResetEvent(false);
        private bool closed = false;
        private bool recover = false;
        private LayoutManager layoutManager;
        private ToolStripButton btnOcrPrecache;
        private ToolStripButton btnOcrCancel;
        private bool ocrPrecacheRunning;
        private bool ocrCancelRequested;
    
        private Bitmap bitmap; // Used for the preview window
        private bool splitter1 = false; // Used for the splitter GUI state of display
        private bool insert = false; //Used to determine if the scanning will insert images or append them a the end
        private int insertCounter = 0; //Used to count the offset of images to insert
        private DirectImageTransfer copiedImages; // Last images copied from the thumbnail list, used by "Paste after this image"
        private string title = System.Windows.Forms.Application.ProductName.ToString() + " " + System.Windows.Forms.Application.ProductVersion.ToString();
        private Size Oldsize = Size.Empty;

        public static bool darkMode = false;
        public static FDesktop instance = null;

        //For document management
        public List<Document> documents;

        // Variables as static
        public static string projectName = string.Format(MiscResources.ProjectName);
        public List<ProjectSettings> projectsConfig;
        #endregion

        #region Initialization and Culture

        public FDesktop(ImageSettingsContainer imageSettingsContainer, StringWrapper stringWrapper, AppConfigManager appConfigManager, RecoveryManager recoveryManager, OcrManager ocrManager, IProfileManager profileManager, IScanPerformer scanPerformer, IScannedImagePrinter scannedImagePrinter, ChangeTracker changeTracker, StillImage stillImage, IOperationFactory operationFactory, IUserConfigManager userConfigManager, KeyboardShortcutManager ksm, ThumbnailRenderer thumbnailRenderer, WinFormsExportHelper exportHelper, ScannedImageRenderer scannedImageRenderer, NotificationManager notify, CultureInitializer cultureInitializer, IWorkerServiceFactory workerServiceFactory, IOperationProgress operationProgress, ScannedImageHelper scannedImageHelper)
        {
            this.stringWrapper = stringWrapper;
            this.appConfigManager = appConfigManager;
            this.recoveryManager = recoveryManager;
            this.ocrManager = ocrManager;
            this.profileManager = profileManager;
            this.scanPerformer = scanPerformer;
            this.scannedImagePrinter = scannedImagePrinter;
            this.changeTracker = changeTracker;
            this.stillImage = stillImage;
            this.operationFactory = operationFactory;
            this.userConfigManager = userConfigManager;
            this.ksm = ksm;
            this.thumbnailRenderer = thumbnailRenderer;
            this.exportHelper = exportHelper;
            this.scannedImageRenderer = scannedImageRenderer;
            this.notify = notify;
            this.cultureInitializer = cultureInitializer;
            this.workerServiceFactory = workerServiceFactory;
            this.operationProgress = operationProgress;
            this.imageSettingsContainer = imageSettingsContainer;
            this.scannedImageHelper = scannedImageHelper;

            InitializeComponent();

            //get the initial document count.
            documents = new List<Document> { };

            // Creating a static pointer to this, so we can refer (test)
            instance = this;

            title = System.Windows.Forms.Application.ProductName.ToString() + " " + System.Windows.Forms.Application.ProductVersion.ToString();
            if (Text != null)
                this.Text = title + " | " + MiscResources.ProjectNameTitle + projectName;

            notify.ParentForm = this;
            Shown += FDesktop_Shown;
            FormClosing += FDesktop_FormClosing;
            Closed += FDesktop_Closed;

            ImageSettingsContainer.ProjectSettings = new ProjectSettings();
            if (ImageSettingsContainer.ProjectSettings.Name == "")
                ImageSettingsContainer.ProjectSettings.Name = ImageSettingsContainer.ProjectSettings.BatchName;

            //Add an event handler to the barcodeInfo
            TS_BarcodeInfo.TextBox.KeyPress +=new System.Windows.Forms.KeyPressEventHandler(BarCodeInfo_KeyPress);
            

        }

        public static FDesktop GetInstance()
        {
            //this will get the instance to this class and be able to call functions like a manager class
            //MSVC will Reference where it was required
            return instance;
        }
        public void SetProjectConfigs(List<ProjectSettings> theList)
        {
            List<ProjectSettings> projectsConfig = new List<ProjectSettings>(theList);
        }

        public List<ProjectSettings> GetProjectConfigs()
        {
            
            return projectsConfig;
        }
       
        protected override void OnLoad(object sender, EventArgs eventArgs)
        {
            PostInitializeComponent();
            InitOcrPrecacheButtons();
        }

        public void SetRecover(bool recovery)
        {
            //This is used the tell this class that it's in recovery mode.
            //Once the recovery is completed, it will be put to false
            //Selecting an item, while recovering was perceived like a rescan from file. It's not allowed now using this method.
            if (!recovery)
            {
                // Recovery ended or was cancelled: add any remaining buffered pages
                FlushRecoveredImages();
            }
            recover = recovery;
        }

        /// <summary>
        /// Runs when the form is first loaded and every time the language is changed.
        /// </summary>
        private ToolStripMenuItem tsBarCodeFilter;

        private void PostInitializeComponent()
        {
            foreach (var panel in toolStripContainer1.Controls.OfType<ToolStripPanel>())
            {
                ToolStripPanelSetStyle.Invoke(panel, new object[] { ControlStyles.Selectable, true });
            }

            // Entrée « Filtre des codes-barres… » dans le menu image, juste après la détection.
            // Ajoutée une seule fois : cette méthode est aussi rappelée lors d'un changement de langue.
            if (tsBarCodeFilter == null)
            {
                tsBarCodeFilter = new ToolStripMenuItem("Options des codes-barres...");
                tsBarCodeFilter.Click += tsBarCodeFilter_Click;
                int index = tsdImage.DropDownItems.IndexOf(tsBarCodeCheck);
                tsdImage.DropDownItems.Insert(index + 1, tsBarCodeFilter);
            }
            imageList.ThumbnailRenderer = thumbnailRenderer;
            thumbnailList1.ThumbnailRenderer = thumbnailRenderer;
            int thumbnailSize = UserConfigManager.Config.ThumbnailSize;

            splitContainer1.Panel2Collapsed = UserConfigManager.Config.Quickview;

            splitContainer1.SplitterDistance = UserConfigManager.Config.Splitter1_distance;

            TS_Index.Visible = UserConfigManager.Config.IndexWindow;

            thumbnailList1.ThumbnailSize = new Size(thumbnailSize, thumbnailSize);
            SetThumbnailSpacing(thumbnailSize);


            darkMode = SetDarkMode(UserConfigManager.Config.DarkMode);

            if (appConfigManager.Config.HideOcrButton)
            {
                //tStrip.Items.Remove(tsOcr_1);
            }
            if (appConfigManager.Config.HideImportButton)
            {
                FileDm.DropDownItems.Remove(tsImport);
            }
            if (appConfigManager.Config.HideSavePdfButton)
            {
                FileDm.DropDownItems.Remove(tsdSavePDF);
            }
            if (appConfigManager.Config.HideSaveImagesButton)
            {
                FileDm.DropDownItems.Remove(tsdSaveImages);
            }
            if (appConfigManager.Config.HidePrintButton)
            {
                tStrip.Items.Remove(printToolStripMenuItem);
            }

            LoadToolStripLocation();
            RelayoutToolbar();
            InitLanguageDropdown();
            AssignKeyboardShortcuts();
            updateProfileButton();
            

            layoutManager?.Deactivate();
            btnZoomIn.Location = new Point(btnZoomIn.Location.X, thumbnailList1.Height - 44);
            btnZoomOut.Location = new Point(btnZoomOut.Location.X, thumbnailList1.Height - 44);
            btnZoomMouseCatcher.Location = new Point(btnZoomMouseCatcher.Location.X, thumbnailList1.Height - 44);
            layoutManager = new LayoutManager(this)
                  .Bind(btnZoomIn, btnZoomOut, btnZoomMouseCatcher)
                       .BottomTo(() => thumbnailList1.Height)
                   .Activate();
           
            thumbnailList1.MouseWheel += ThumbnailList1_MouseWheel;
            thumbnailList1.SizeChanged += (sender, args) => layoutManager.UpdateLayout();

            
            

        }

        private void InitLanguageDropdown()
        {
            // Read a list of languages from the Languages.resx file
            var resourceManager = LanguageNames.ResourceManager;
            var resourceSet = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
            foreach (DictionaryEntry entry in resourceSet.Cast<DictionaryEntry>().OrderBy(x => x.Value))
            {
                var langCode = ((string)entry.Key).Replace("_", "-");
                var langName = (string)entry.Value;

                // Only include those languages for which localized resources exist
                string localizedResourcesPath =
                    Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", langCode,
                        "NAPS2.Core.resources.dll");
                if (langCode == "en" || File.Exists(localizedResourcesPath))
                {
                    var button = new ToolStripMenuItem(langName, null, (sender, args) => SetCulture(langCode));
                    tsLanguage.DropDownItems.Add(button);
                }
            }
        }

        private void RelayoutToolbar()
        {
            // Resize and wrap text as necessary
            using (var g = CreateGraphics())
            {
                foreach (var btn in tStrip.Items.OfType<ToolStripItem>())
                {
                    if (PlatformCompat.Runtime.SetToolbarFont)
                    {
                        btn.Font = new Font("Segoe UI", 10);
                    }
                    btn.Text = stringWrapper.Wrap(btn.Text ?? "", 80, g, btn.Font);
                }
            }
            ResetToolbarMargin();
            // Recalculate visibility for the below check
            System.Windows.Forms.Application.DoEvents();
           
            // Check if toolbar buttons are overflowing
            if (tStrip.Items.OfType<ToolStripItem>().Any(btn => !btn.Visible)
                && (tStrip.Parent.Dock == DockStyle.Top || tStrip.Parent.Dock == DockStyle.Bottom))
            {
                ShrinkToolbarMargin();
            }
        }

        private void ResetToolbarMargin()
        {
            foreach (var btn in tStrip.Items.OfType<ToolStripItem>())
            {
                if (btn is ToolStripSplitButton)
                {
                    if (tStrip.Parent.Dock == DockStyle.Left || tStrip.Parent.Dock == DockStyle.Right)
                    {
                        btn.Margin = new Padding(10, 1, 5, 2);
                    }
                    else
                    {
                        btn.Margin = new Padding(5, 1, 5, 2);
                    }
                }
                else if (btn is ToolStripDoubleButton)
                {
                    btn.Padding = new Padding(5, 0, 5, 0);
                }
                else if (tStrip.Parent.Dock == DockStyle.Left || tStrip.Parent.Dock == DockStyle.Right)
                {
                    btn.Margin = new Padding(0, 1, 5, 2);
                }
                else
                {
                    btn.Padding = new Padding(10, 0, 10, 0);
                }
            }
        }

        private void ShrinkToolbarMargin()
        {
            foreach (var btn in tStrip.Items.OfType<ToolStripItem>())
            {
                if (btn is ToolStripSplitButton)
                {
                    btn.Margin = new Padding(0, 1, 0, 2);
                }
                else if (btn is ToolStripDoubleButton)
                {
                    btn.Padding = new Padding(0, 0, 0, 0);
                }
                else
                {
                    btn.Padding = new Padding(5, 0, 5, 0);
                }
            }
        }

        private void SetCulture(string cultureId)
        {
            SaveToolStripLocation();
            UserConfigManager.Config.Culture = cultureId;
            UserConfigManager.Save();
            cultureInitializer.InitCulture(Thread.CurrentThread);

            // Update localized values
            // Since all forms are opened modally and this is the root form, it should be the only one that needs to be updated live
            SaveFormState = false;
            Controls.Clear();
            UpdateRTL();
            InitializeComponent();
            PostInitializeComponent();
            
            AddThumbnails();
            notify.Rebuild();
            Focus();
            WindowState = FormWindowState.Normal;
            DoRestoreFormState();
            SaveFormState = true;

            UpdateToolbar();
        }

        private async void FDesktop_Shown(object sender, EventArgs e)
        {
            UpdateToolbar();

            // Receive messages from other processes
            Pipes.StartServer(msg =>
            {
                if (msg.StartsWith(Pipes.MSG_SCAN_WITH_DEVICE, StringComparison.InvariantCulture))
                {
                    SafeInvoke(async () => await ScanWithDevice(msg.Substring(Pipes.MSG_SCAN_WITH_DEVICE.Length)));
                }
                if (msg.Equals(Pipes.MSG_ACTIVATE))
                {
                    SafeInvoke(() =>
                    {
                        var form = System.Windows.Forms.Application.OpenForms.Cast<Form>().Last();
                        if (form.WindowState == FormWindowState.Minimized)
                        {
                            Win32.ShowWindow(form.Handle, Win32.ShowWindowCommands.Restore);
                        }
                        form.Activate();
                    });
                }
            });

            // If configured (e.g. by a business), show a customizable message box on application startup.
            var appConfig = appConfigManager.Config;
            if (!string.IsNullOrWhiteSpace(appConfig.StartupMessageText))
            {
                MessageBox.Show(appConfig.StartupMessageText, appConfig.StartupMessageTitle, MessageBoxButtons.OK,
                    appConfig.StartupMessageIcon);
            }


            // Allow scanned images to be recovered in case of an unexpected close
            CheckUnfinishedWorkFolders();

            StartThumbnailRendering();

            // If NAPS2 was started by the scanner button, do the appropriate actions automatically
            await RunStillImageEvents();

            // Show a donation prompt after a month of use
            if (userConfigManager.Config.FirstRunDate == null)
            {
                userConfigManager.Config.FirstRunDate = DateTime.Now;
                userConfigManager.Save();
            }
#if !INSTALLER_MSI
            else if (!appConfigManager.Config.HideDonateButton &&
                userConfigManager.Config.LastDonatePromptDate == null &&
                DateTime.Now - userConfigManager.Config.FirstRunDate > TimeSpan.FromDays(30))
            {
                userConfigManager.Config.LastDonatePromptDate = DateTime.Now;
                userConfigManager.Save();
                //notify.DonatePrompt();
            }

#endif
        }

        #endregion

        #region Cleanup

        private void FDesktop_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (closed) return;

            // There is an operation in progress while the user want to close the application
            if (operationProgress.ActiveOperations.Any())
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    if (operationProgress.ActiveOperations.Any(x => !x.SkipExitPrompt))
                    {
                        var result = MessageBox.Show(MiscResources.ExitWithActiveOperations, MiscResources.ActiveOperations,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                        if (result != DialogResult.Yes)
                        {
                            e.Cancel = true;
                        }
                    }
                }
                else
                {
                    //RecoveryImage.DisableRecoveryCleanup = true;
                }
            } // Or there are unsaved changes
            else if (changeTracker.HasUnsavedChanges)
            {
                if (e.CloseReason == CloseReason.UserClosing && !RecoveryImage.DisableRecoveryCleanup)
                {
                    var result = MessageBox.Show(MiscResources.ExitWithUnsavedChanges, MiscResources.UnsavedChanges,
                        MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (result == DialogResult.No)
                    {
                        // User want to close and delete the current work
                        changeTracker.Clear();
                        
                        bitmap?.Dispose();

                        DeleteAllImagesKeepingProject();
                        

                    }
                    else if (result ==DialogResult.Yes)
                    {
                        closeWorkspace();
                    }
                    else
 
                    {
                       e.Cancel = true;
                    }
                }
                else
                {
                    //RecoveryImage.DisableRecoveryCleanup = true;
                }
            }

            if (!e.Cancel && operationProgress.ActiveOperations.Any())
            {
                operationProgress.ActiveOperations.ForEach(op => op.Cancel());
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                Task.Factory.StartNew(() =>
                {
                    var timeoutCts = new CancellationTokenSource();
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
                    try
                    {
                        operationProgress.ActiveOperations.ForEach(op => op.Wait(timeoutCts.Token));
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    closed = true;
                    SafeInvoke(Close);
                });
            }
            
            bitmap?.Dispose();

        }

        private void FDesktop_Closed(object sender, EventArgs e)
        {
            SaveToolStripLocation();
            Pipes.KillServer();
            //Remove the work folder when closing the application
            DeleteAllImagesKeepingProject();
            RecoveryImage.DeleteWorkFolder();

            closed = true;
            renderThumbnailsWaitHandle.Set();
            tiffViewerCtl1.Dispose();
        }

        #endregion

        #region Scanning and Still Image

        private async Task RunStillImageEvents()
        {
            if (stillImage.ShouldScan)
            {
                await ScanWithDevice(stillImage.DeviceID);
            }
        }

        private async Task ScanWithDevice(string deviceID)
        {
            Activate();
            ScanProfile profile;
            
            if (profileManager.DefaultProfile?.Device?.ID == deviceID)
            {
                // Try to use the default profile if it has the right device
                profile = profileManager.DefaultProfile;
            }
            else
            {
                // Otherwise just pick any old profile with the right device
                // Not sure if this is the best way to do it, but it's hard to prioritize profiles
                profile = profileManager.Profiles.FirstOrDefault(x => x.Device != null && x.Device.ID == deviceID);
            }
            if (profile == null)
            {
                if (appConfigManager.Config.NoUserProfiles && profileManager.Profiles.Any(x => x.IsLocked))
                {
                    return;
                }

                // No profile for the device we're scanning with, so prompt to create one
                var editSettingsForm = FormFactory.Create<FEditProfile>();
                BackgroundForm.UseImmersiveDarkMode(editSettingsForm.Handle, darkMode);
                editSettingsForm.ScanProfile = appConfigManager.Config.DefaultProfileSettings ??
                                               new ScanProfile { Version = ScanProfile.CURRENT_VERSION };
                try
                {
                    // Populate the device field automatically (because we can do that!)
                    using (var deviceManager = new WiaDeviceManager())
                    using (var device = deviceManager.FindDevice(deviceID))
                    {
                        editSettingsForm.CurrentDevice = new ScanDevice(deviceID, device.Name());
                    }
                }
                catch (WiaException)
                {
                }
                editSettingsForm.ShowDialog();
                if (!editSettingsForm.Result)
                {
                    return;
                }
                profile = editSettingsForm.ScanProfile;
                profileManager.Profiles.Add(profile);
                profileManager.DefaultProfile = profile;
                profileManager.Save();
                updateProfileButton();
            }
            if (profile != null)
            {
                // We got a profile, yay, so we can actually do the scan now
                await scanPerformer.PerformScan(profile, new ScanParams(), this, ReceiveScannedImage(true));
                RunAutoBarcodeDetection();
                Activate();
            }
        }

        public async Task ScanDefault()
        {
            ScanParams param = new ScanParams();
            
            if (SelectedIndices.Any()  && !this.insert)
            {
                if (MessageBox.Show(string.Format(MiscResources.Rescan), string.Format(MiscResources.Rescan_title), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                    param.RescanMode = true; //In rescan, it only scan one picture and will replace it.
                else
                    return;
            } 

            if (profileManager.DefaultProfile != null)
            {
                await scanPerformer.PerformScan(profileManager.DefaultProfile, param, this, ReceiveScannedImage(true));
                param.RescanMode = false;
                RunAutoBarcodeDetection();
            }
            
            else if (profileManager.Profiles.Count == 0)
            {
                await ScanWithNewProfile();
            }
            else
            {
               ShowProfilesForm();
            }

            Activate();
        }

        private async Task ScanWithNewProfile()
        {
            var editSettingsForm = FormFactory.Create<FEditProfile>();
            BackgroundForm.UseImmersiveDarkMode(editSettingsForm.Handle, darkMode);
            editSettingsForm.ScanProfile = appConfigManager.Config.DefaultProfileSettings ?? new ScanProfile { Version = ScanProfile.CURRENT_VERSION };
            editSettingsForm.ShowDialog();
            if (!editSettingsForm.Result)
            {
                return;
            }
            profileManager.Profiles.Add(editSettingsForm.ScanProfile);
            profileManager.DefaultProfile = editSettingsForm.ScanProfile;
            profileManager.Save();
            updateProfileButton();

            await scanPerformer.PerformScan(editSettingsForm.ScanProfile, new ScanParams(), this, ReceiveScannedImage(true));
            RunAutoBarcodeDetection();

            Activate();
        }

        #endregion

        #region Images and Thumbnails

        private IEnumerable<int> SelectedIndices
        {
            // A copy: the native selection is read once, not again for every Count()/Any()/First() of the caller
            get => thumbnailList1.SelectedIndices.Cast<int>().ToArray();
            set
            {
                // Read before clearing: the new selection may be computed from the current one
                var indices = value.ToList();
                int count = thumbnailList1.Items.Count;
                // Every Add raises SelectedIndexChanged: they are ignored here and the update runs once at the end
                selectionChanging = true;
                try
                {
                    thumbnailList1.SelectedIndices.Clear();
                    foreach (int i in indices)
                    {
                        // An index without an item (not created yet) used to throw here
                        if (i >= 0 && i < count)
                        {
                            thumbnailList1.SelectedIndices.Add(i);
                        }
                    }
                }
                finally
                {
                    selectionChanging = false;
                }
                DisplaySelectedItem_info();
            }
        }

        private IEnumerable<ScannedImage> SelectedImages => imageList.Images.ElementsAt(SelectedIndices);

        // Check for barcode Data CC
        // Also check for getting more information about the images, not just the barcode

        public void setPreviewImage(Bitmap image, bool frontpage = true)
        {
            if (frontpage)
            {

                tiffViewerCtl1.Image = image;
                tiffViewerCtl1.Refresh();
            }
            else
            {
                tiffViewerCtl1.Image2 = image;
                tiffViewerCtl1.Refresh();
            }
        }

        // Preview: the page and version (transform state) currently shown in the viewer, and the latest request.
        // All of these, like previewPrefetch, are only used on the UI thread.
        private ScannedImage previewSource;
        private int previewState = -1;
        private int previewRequest;

        // Pages next to the one shown (previous and next), rendered in advance so moving through the pages is instant
        private readonly Dictionary<ScannedImage, (int State, Task<Bitmap> Render)> previewPrefetch =
            new Dictionary<ScannedImage, (int State, Task<Bitmap> Render)>();

        private async void GetPreviewImage(ScannedImage img, bool updateGUI)
        {
            if (updateGUI == false)
                return;

            int request = ++previewRequest;
            Task<Bitmap> render;
            int state;
            try
            {
                using (var snapshot = img.Preserve())
                {
                    state = snapshot.TransformState;
                }
                // Same page and same version already shown (e.g. its thumbnail was just rendered): nothing to do
                if (ReferenceEquals(img, previewSource) && state == previewState && bitmap != null && ReferenceEquals(tiffViewerCtl1.Image, bitmap))
                {
                    return;
                }
                render = TakePrefetchedPreview(img, state) ?? RenderPreview(img, out state);
            }
            catch (ObjectDisposedException)
            {
                // The page was removed
                return;
            }

            Bitmap result;
            try
            {
                result = await render;
            }
            catch (Exception e)
            {
                Log.ErrorException("Error rendering the preview", e);
                return;
            }

            // Another page was selected meanwhile: only the latest request is shown
            if (request != previewRequest || closed)
            {
                result.Dispose();
                return;
            }

            var old = bitmap;
            bitmap = result;
            previewSource = img;
            previewState = state;
            setPreviewImage(bitmap, true);
            // The previous preview is no longer shown (it was never freed before)
            if (old != null && !ReferenceEquals(old, bitmap))
            {
                old.Dispose();
            }

            UpdatePreviewInfo(img, bitmap);
            // The status bar was written before the size and format were known
            if (SelectedIndices.Count() == 1 && ReferenceEquals(SelectedImages.FirstOrDefault(), img))
            {
                DisplaySelectedItem_info(true);
            }

            PrefetchNeighbourPreviews(img);
        }

        private static void UpdatePreviewInfo(ScannedImage img, Bitmap bmp)
        {
            if (bmp != null)
            {
                Size size = bmp.Size;
                img.infoResolution = size.Width + " px X " + size.Height + " px ";

                string dpi = Math.Round(bmp.HorizontalResolution).ToString();

                string format = "Format: ";
                format = bmp.PixelFormat switch
                {
                    PixelFormat.Format24bppRgb => format + "Color 24bit, DPI: " + dpi,
                    PixelFormat.Format32bppArgb => format + "Color 32bit, DPI: " + dpi,
                    PixelFormat.Format8bppIndexed => format + "Indexed Color 8bit, DPI: " + dpi,
                    PixelFormat.Format1bppIndexed => format + "Bitonal, DPI: " + dpi,
                    _ => "DPI: " + dpi,
                };
                img.infoFormat = format;
            }
            else
            {
                img.infoResolution = "";
                img.infoFormat = "";
            }
        }

        // Starts rendering the full page in the background. The page file is read into memory (see
        // ScannedImageRenderer.RenderNow): no file lock is kept, so the bitmap no longer needs to be copied.
        private Task<Bitmap> RenderPreview(ScannedImage img, out int state)
        {
            var snapshot = img.Preserve();
            state = snapshot.TransformState;
            return Task.Run(() =>
            {
                using (snapshot)
                {
                    return scannedImageRenderer.RenderNow(snapshot);
                }
            });
        }

        // Returns the page rendered in advance if it is still the same version, otherwise null
        private Task<Bitmap> TakePrefetchedPreview(ScannedImage img, int state)
        {
            if (previewPrefetch.TryGetValue(img, out var entry))
            {
                previewPrefetch.Remove(img);
                if (entry.State == state)
                {
                    return entry.Render;
                }
                DisposeWhenRendered(entry.Render);
            }
            return null;
        }

        private void PrefetchNeighbourPreviews(ScannedImage img)
        {
            var wanted = new List<ScannedImage>();
            lock (imageList)
            {
                int index = imageList.Images.IndexOf(img);
                if (index == -1)
                {
                    return;
                }
                if (index + 1 < imageList.Images.Count)
                {
                    wanted.Add(imageList.Images[index + 1]);
                }
                if (index > 0)
                {
                    wanted.Add(imageList.Images[index - 1]);
                }
            }

            // Only the two neighbours are kept: a full page takes a lot of memory
            foreach (var key in previewPrefetch.Keys.Where(x => !wanted.Contains(x)).ToList())
            {
                DisposeWhenRendered(previewPrefetch[key].Render);
                previewPrefetch.Remove(key);
            }
            foreach (var page in wanted)
            {
                if (previewPrefetch.ContainsKey(page))
                {
                    // Already rendered or rendering (its version is checked when it is taken)
                    continue;
                }
                try
                {
                    var task = RenderPreview(page, out int pageState);
                    previewPrefetch[page] = (pageState, task);
                }
                catch (ObjectDisposedException)
                {
                    // The page was removed
                }
            }
        }

        private void ClearPreviewPrefetch()
        {
            foreach (var entry in previewPrefetch.Values)
            {
                DisposeWhenRendered(entry.Render);
            }
            previewPrefetch.Clear();
            previewSource = null;
            previewState = -1;
        }

        private static void DisposeWhenRendered(Task<Bitmap> render)
        {
            render.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    t.Result.Dispose();
                }
            });
        }

        // Recovered pages are buffered and added to the UI in batches, instead of one UI round-trip per page
        private const int RecoveryBatchSize = 25;
        private readonly List<ScannedImage> pendingRecoveredImages = new List<ScannedImage>();

        public Action<ScannedImage> ReceiveRecovery()
        {
            return scannedImage =>
            {
                bool flush;
                lock (pendingRecoveredImages)
                {
                    pendingRecoveredImages.Add(scannedImage);
                    flush = pendingRecoveredImages.Count >= RecoveryBatchSize;
                }
                if (flush)
                {
                    FlushRecoveredImages();
                }
            };
        }

        // Adds the buffered recovered pages to the list in a single UI update
        private void FlushRecoveredImages()
        {
            List<ScannedImage> batch;
            lock (pendingRecoveredImages)
            {
                if (pendingRecoveredImages.Count == 0)
                {
                    return;
                }
                batch = pendingRecoveredImages.ToList();
                pendingRecoveredImages.Clear();
            }

            SafeInvoke(() =>
            {
                lock (imageList)
                {
                    foreach (var image in batch)
                    {
                        imageList.Images.Add(image);
                        image.ThumbnailChanged += ImageThumbnailChanged;
                        image.ThumbnailInvalidated += ImageThumbnailInvalidated;
                    }
                    AddThumbnails();
                }
                UpdateToolbar();
                changeTracker.Made();
            });

            // Trigger thumbnail rendering for the newly added pages
            renderThumbnailsWaitHandle.Set();
        }

        /// <summary>
        /// Constructs a receiver for scanned images.
        /// This keeps images from the same source together, even if multiple sources are providing images at the same time.
        /// </summary>
        /// <returns></returns>
        /// <param name="autoBarcode">Vrai pour une numérisation : les pages reçues sont mémorisées pour la détection
        /// automatique des codes-barres lancée à la fin du scan (voir RunAutoBarcodeDetection).</param>
        public Action<ScannedImage> ReceiveScannedImage(bool autoBarcode = false)
        {
            ScannedImage last = null;
            return scannedImage =>
            {
                if (autoBarcode && userConfigManager.Config.BarcodeAutoDetectAfterScan)
                {
                    lock (barcodeAutoPending)
                    {
                        barcodeAutoPending.Add(scannedImage);
                    }
                }

                // Queue the page and return at once, so the scan thread does not wait for the UI for every page.
                // The queue is drained in order on the UI thread, in one pass per batch (see ProcessScannedImageQueue).
                lock (scannedImageQueue)
                {
                    scannedImageQueue.Enqueue(new QueuedScannedImage(scannedImage, last));
                    last = scannedImage;
                    if (scannedImageQueueScheduled)
                    {
                        return;
                    }
                    scannedImageQueueScheduled = true;
                }
                try
                {
                    BeginInvoke((Action)ProcessScannedImageQueue);
                }
                catch (Exception e) when (e is ObjectDisposedException || e is InvalidOperationException)
                {
                    // The form is closing or its handle is not available. Drop the schedule flag so later pages can
                    // schedule again, instead of leaving the queue stuck with the flag set.
                    lock (scannedImageQueue)
                    {
                        scannedImageQueueScheduled = false;
                    }
                }
            };
        }

        // Pages reçues pendant une numérisation, en attente de la détection automatique des codes-barres.
        // Accédé depuis le fil de numérisation et le fil de l'interface : toujours sous lock(barcodeAutoPending).
        private readonly List<ScannedImage> barcodeAutoPending = new List<ScannedImage>();

        /// <summary>
        /// Lance en arrière-plan la détection des codes-barres sur les pages de la numérisation qui vient de se terminer.
        /// Ne fait rien si l'option est désactivée (l'utilisateur peut toujours lancer la détection à la main).
        /// Le statut s'affiche dans le widget de progression en bas à droite, comme les autres traitements en fond.
        /// </summary>
        private void RunAutoBarcodeDetection()
        {
            List<ScannedImage> images;
            lock (barcodeAutoPending)
            {
                images = barcodeAutoPending.ToList();
                barcodeAutoPending.Clear();
            }
            if (images.Count == 0 || !userConfigManager.Config.BarcodeAutoDetectAfterScan)
            {
                return;
            }

            // À la fin : documents créés selon les codes lus (si l'option est active) et texte du code rafraîchi
            var op = CreateBarcodeOperation(images);
            if (op.Start(images))
            {
                operationProgress.ShowBackgroundProgress(op);
                changeTracker.Made();
            }
        }

        // A page waiting for the UI, plus the page it followed when it was received (used for placement)
        private class QueuedScannedImage
        {
            public QueuedScannedImage(ScannedImage image, ScannedImage previous)
            {
                Image = image;
                Previous = previous;
            }

            public ScannedImage Image { get; }

            public ScannedImage Previous { get; }
        }

        // Pages received from scanning/importing, waiting to be placed on the UI thread.
        // scannedImageQueueScheduled is true while a ProcessScannedImageQueue call is pending. Both fields are
        // only accessed under lock(scannedImageQueue).
        private readonly Queue<QueuedScannedImage> scannedImageQueue = new Queue<QueuedScannedImage>();
        private bool scannedImageQueueScheduled;

        // Set while placing a batch to request one thumbnail refresh at the end. Only used on the UI thread.
        private bool pendingThumbnailRefresh;

        // Runs on the UI thread. Places every queued page in arrival order, then updates the UI once for the batch.
        private void ProcessScannedImageQueue()
        {
            List<QueuedScannedImage> batch;
            lock (scannedImageQueue)
            {
                batch = scannedImageQueue.ToList();
                scannedImageQueue.Clear();
                scannedImageQueueScheduled = false;
            }
            if (batch.Count == 0)
            {
                return;
            }

            bool anyPlaced = false;
            pendingThumbnailRefresh = false;
            foreach (var item in batch)
            {
                anyPlaced |= PlaceScannedImage(item.Image, item.Previous);
            }

            if (pendingThumbnailRefresh)
            {
                // A single thumbnail refresh covers every page added in this batch
                AddThumbnails();
            }
            if (anyPlaced)
            {
                // Groups are rebuilt by AddedImages once the new list items exist (see AddThumbnails).
                // Calling GroupRefresh here, before those items exist, left the ListView suspended and blank.
                UpdateToolbar();
                changeTracker.Made();
            }
            // Trigger thumbnail rendering just in case the received images are out of date
            renderThumbnailsWaitHandle.Set();
        }

        // Places one received page in the list. Same placement rules as before; runs on the UI thread.
        private bool PlaceScannedImage(ScannedImage scannedImage, ScannedImage previous)
        {
            lock (imageList)
            {
                // Default to the end of the list
                if (!this.insert) // Reset the page counter for insertion since it's not in insert mode.
                    insertCounter = 0;

                // Placement follows the previous page from the same source; the insert/rescan branches below
                // decide the final position, as before.
                bool thumbnailsChanged;
                if (SelectedIndices.Any() && !this.insert && !recover) // rescan will replace image only not add anything
                {
                    //Rescan mode
                    var origSide = imageList.Images[SelectedIndices.First()].SheetSide; //The face of the page on the sheet must be preserved
                    imageList.Delete(Enumerable.Range(SelectedIndices.First(), 1));
                    imageList.Images.Insert(SelectedIndices.First(), scannedImage);
                    scannedImage.MovedTo(SelectedIndices.First());
                    scannedImage.SheetSide = origSide;
                    UpdateThumbnails(Enumerable.Range(SelectedIndices.First(), 1), true, true);
                    thumbnailsChanged = false;
                }
                else
                {
                    // Insert Mode
                    if (this.insert) //Try to insert
                    {
                        int index = SelectedIndices.First() + this.insertCounter;
                        insertCounter++;
                        imageList.Images.Insert(index, scannedImage);
                        scannedImage.MovedTo(index);
                    }
                    else
                    {
                        imageList.Images.Add(scannedImage);
                    }
                    thumbnailsChanged = true;
                }
                scannedImage.ThumbnailChanged += ImageThumbnailChanged;
                scannedImage.ThumbnailInvalidated += ImageThumbnailInvalidated;

                if (thumbnailsChanged)
                {
                    // One thumbnail refresh is started per batch (see ProcessScannedImageQueue)
                    pendingThumbnailRefresh = true;
                }
                return true;
            }
        }

        /// <summary>
        /// Creates the list items (and icons) of the pages added to imageList. Runs synchronously on the UI thread:
        /// it used to run later (Task.Run then Invoke), so a thumbnail or a selection could refer to an item
        /// that did not exist yet. That threw in the middle of a BeginUpdate (the list then stopped redrawing)
        /// and the thumbnail was lost (the page kept its hourglass).
        /// </summary>
        private void AddThumbnails()
        {
            if (InvokeRequired)
            {
                SafeInvoke(AddThumbnails);
                return;
            }
            if (closed)
            {
                return;
            }
            lock (imageList)
            {
                thumbnailList1.AddedImages(imageList.Images, Color.Black, recover);
            }

            //Scroll the list so that every new item that get added can be viewed. -CC
            //Should not do it if a selection is active.
            if (thumbnailList1.Items.Count > 5 && thumbnailList1.SelectedIndices.Count == 0 && !recover)
                thumbnailList1.EnsureVisible(thumbnailList1.Items.Count - 1);
        }

        private void DeleteThumbnails()
        {
            Color color = Color.Black;
            thumbnailList1.DeletedImages(imageList.Images);
            thumbnailList1.Invoke(new MethodInvoker(delegate
            {
               
                thumbnailList1.RegenerateThumbnailList(imageList.Images, color, true);
            }));
            
        }

        private void UpdateThumbnails(IEnumerable<int> selection, bool scrollToSelection, bool optimizeForSelection)
        {
            Color fore = Color.Black;
            thumbnailList1.UpdatedImages(imageList.Images, optimizeForSelection ? SelectedIndices.Concat(selection).ToList() : null, fore);
            SelectedIndices = selection;

             if (scrollToSelection)
            {
                // Scroll to selection
                // If selection is empty (e.g. after interleave), this scrolls to top
                if (!recover)
                { 
                    thumbnailList1.EnsureVisible(SelectedIndices.LastOrDefault());
                    thumbnailList1.EnsureVisible(SelectedIndices.FirstOrDefault());
                }
            }
        }

        // Pages whose icon must be redrawn. Filled from any thread, applied in one pass on the UI thread by the
        // timer (FlushThumbnailUpdates). Before, every thumbnail posted its own message to the UI, which redrew the
        // whole list; while thumbnails kept coming, Windows never got to repaint the list (posted messages go
        // before painting), so the icons only appeared at the end.
        private readonly HashSet<ScannedImage> pendingThumbnailUpdates = new HashSet<ScannedImage>();

        private void ImageThumbnailChanged(object sender, EventArgs e)
        {
            lock (pendingThumbnailUpdates)
            {
                pendingThumbnailUpdates.Add((ScannedImage)sender);
            }
        }

        private void ImageThumbnailInvalidated(object sender, EventArgs e)
        {
            var image = (ScannedImage)sender;
            // The page changed: if its thumbnail had failed, try again
            lock (failedThumbnails)
            {
                failedThumbnails.Remove(image);
            }
            // Shows the hourglass on the current icon until the new thumbnail is rendered
            lock (pendingThumbnailUpdates)
            {
                pendingThumbnailUpdates.Add(image);
            }
            renderThumbnailsWaitHandle.Set();
        }

        // Runs on the UI thread (timer): redraws the icons of every page changed since the last pass
        private void FlushThumbnailUpdates()
        {
            if (closed)
            {
                return;
            }
            List<ScannedImage> changed;
            lock (pendingThumbnailUpdates)
            {
                if (pendingThumbnailUpdates.Count == 0)
                {
                    return;
                }
                changed = pendingThumbnailUpdates.ToList();
                pendingThumbnailUpdates.Clear();
            }

            var updates = new List<(int Index, ScannedImage Image)>(changed.Count);
            lock (imageList)
            {
                var images = imageList.Images;
                if (changed.Count <= 8)
                {
                    foreach (var image in changed)
                    {
                        updates.Add((images.IndexOf(image), image));
                    }
                }
                else
                {
                    // Many pages at once: one pass over the list instead of one search per page
                    var wanted = new HashSet<ScannedImage>(changed);
                    for (int i = 0; i < images.Count; i++)
                    {
                        if (wanted.Contains(images[i]))
                        {
                            updates.Add((i, images[i]));
                        }
                    }
                }
            }
            thumbnailList1.ReplaceThumbnails(updates);

            // The page shown in the preview changed: refresh it (GetPreviewImage skips it if the version is the same)
            if (thumbnailList1.SelectedIndices.Count == 1)
            {
                int selected = thumbnailList1.SelectedIndices[0];
                foreach (var (index, image) in updates)
                {
                    if (index == selected)
                    {
                        GetPreviewImage(image, true);
                        break;
                    }
                }
            }
        }

        #endregion

        #region Toolbar

        public void UpdateToolbar()
        {
            // Read once: the selection count comes from the native ListView and this runs very often
            int selectedCount = thumbnailList1.SelectedIndices.Count;
            bool anySelected = selectedCount > 0;
            bool anyImages = imageList.Images.Count > 0;

            //Rename the title to include the name of the current project.
            title = System.Windows.Forms.Application.ProductName.ToString() + " " + System.Windows.Forms.Application.ProductVersion.ToString();
            var windowTitle = title + " | " + MiscResources.ProjectNameTitle + projectName;
            if (Text != null && Text != windowTitle)
                this.Text = windowTitle;
            //this.Update();

            UpdateOcrButtons();

            // "All" dropdown items
            tsSavePDFAll.Text = tsSaveImagesAll.Text = tsReverseAll.Text =
                string.Format(MiscResources.AllCount, imageList.Images.Count);
            tsSavePDFAll.Enabled = tsSaveImagesAll.Enabled = tsReverseAll.Enabled = tsBarCodeCheck.Enabled = tsExport.Enabled = tsdDocument.Enabled = anyImages;

            // "Selected" dropdown items
            tsSavePDFSelected.Text = tsSaveImagesSelected.Text = tsReverseSelected.Text =
                string.Format(MiscResources.SelectedCount, selectedCount);
            tsSavePDFSelected.Enabled = tsSaveImagesSelected.Enabled = tsReverseSelected.Enabled = 
                tsBlackWhite.Enabled = tsBrightnessContrast.Enabled = tsCrop.Enabled = tsMask.Enabled = tsHueSaturation.Enabled = 
                printToolStripMenuItem.Enabled = tsReset.Enabled = tsSharpen.Enabled = tsView.Enabled = tsInsert.Enabled =
                tsiDocumentAdd.Enabled = tsiDocumentRemove.Enabled = TS_BarcodeInfo.Enabled =
                anySelected;

            if (!anyImages && !anySelected)
                tsdImage.Enabled = false;
            else tsdImage.Enabled = true;

            // Top-level toolbar actions
            tsdRotate.Enabled = tsDelete.Enabled = anySelected;
            tsdReorder.Enabled = tsdSavePDF.Enabled = tsdSaveImages.Enabled = printToolStripMenuItem.Enabled = anyImages;
            

            // Context-menu actions
            ctxView.Visible = ctxCopy.Visible = ctxDelete.Visible = ctxSeparator1.Visible = ctxSeparator2.Visible = anySelected;
            ctxSelectAll.Enabled = anyImages;

            // Other
            btnZoomIn.Enabled = anyImages && UserConfigManager.Config.ThumbnailSize < ThumbnailRenderer.MAX_SIZE;
            btnZoomOut.Enabled = anyImages && UserConfigManager.Config.ThumbnailSize > ThumbnailRenderer.MIN_SIZE;
            tsNewProfile.Enabled = !(appConfigManager.Config.NoUserProfiles && profileManager.Profiles.Any(x => x.IsLocked));

            if (PlatformCompat.Runtime.RefreshListViewAfterChange)
            {
                thumbnailList1.Size = new Size(thumbnailList1.Width - 1, thumbnailList1.Height - 1);
                thumbnailList1.Size = new Size(thumbnailList1.Width + 1, thumbnailList1.Height + 1);
            }

        }

        private void SaveToolStripLocation()
        {
            UserConfigManager.Config.DesktopToolStripDock = tStrip.Parent.Dock;
            UserConfigManager.Save();
        }

        private void LoadToolStripLocation()
        {
            var dock = UserConfigManager.Config.DesktopToolStripDock;
            if (dock != DockStyle.None)
            {
                var panel = toolStripContainer1.Controls.OfType<ToolStripPanel>().FirstOrDefault(x => x.Dock == dock);
                if (panel != null)
                {
                    tStrip.Parent = panel;
                }
            }
            tStrip.Parent.TabStop = true;
          
        }

        #endregion

        #region Actions

        private void Clear(bool skiprequest = false)
        {
            if (imageList.Images.Count > 0)
            {
                ClearPreviewPrefetch();
                // Un document en attente appartenait au projet fermé
                pendingBarcodeDocumentName = null;
                if (skiprequest) 
                {
                    tiffViewerCtl1.Image = null;

                    DeleteAllImagesKeepingProject();
                    DeleteThumbnails();
                    changeTracker.Clear();
                    projectName = string.Format(MiscResources.ProjectName);
                    return;
                }
                var box = MessageBox.Show(string.Format(MiscResources.ConfirmClearItems, imageList.Images.Count), MiscResources.Clear, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (box == DialogResult.OK)
                {
                    tiffViewerCtl1.Image = null;

                    DeleteAllImagesKeepingProject();
                    DeleteThumbnails();
                    changeTracker.Clear();
                    projectName = string.Format(MiscResources.ProjectName);
                }
            }
        }

        private void Delete()
        {
            if (SelectedIndices.Any())
            {
                if (MessageBox.Show(string.Format(MiscResources.ConfirmDeleteItems, SelectedIndices.Count()), MiscResources.Delete, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {

                    int value = SelectedIndices.First();
                    tiffViewerCtl1.Image = null;
                    imageList.Delete(SelectedIndices);
                    DeleteThumbnails();

                    if (imageList.Images.Any())
                    {
                        changeTracker.Made();
                    }
                    else
                    {
                        changeTracker.Clear();
                    }
                    if (value < thumbnailList1.Items.Count)
                        SelectedIndices = Enumerable.Range(value, 1);

                }
            }
        }

        private void SelectAll()
        {
            SelectedIndices = Enumerable.Range(0, imageList.Images.Count);
        }

        private void MoveDown()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            UpdateThumbnails(imageList.MoveDown(SelectedIndices), true, true);
            changeTracker.Made();
        }

        private void MoveUp()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            UpdateThumbnails(imageList.MoveUp(SelectedIndices), true, true);
            changeTracker.Made();

        }

        private async Task RotateLeft()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            changeTracker.Made();
            await imageList.RotateFlip(SelectedIndices, RotateFlipType.Rotate270FlipNone);
            changeTracker.Made();

        }

        private async Task RotateRight()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            changeTracker.Made();
            await imageList.RotateFlip(SelectedIndices, RotateFlipType.Rotate90FlipNone);
            changeTracker.Made();
           
        }

        private async Task Flip()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            changeTracker.Made();
            await imageList.RotateFlip(SelectedIndices, RotateFlipType.RotateNoneFlipXY);
            changeTracker.Made();

        }

        private void Deskew()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }

            var op = operationFactory.Create<DeskewOperation>();
            if (op.Start(SelectedImages.ToList()))
            {
                operationProgress.ShowProgress(op);
                changeTracker.Made();
            }
        }

        private void PreviewImage()
        {
            if (SelectedIndices.Any())
            {
                using (var viewer = FormFactory.Create<FViewer>())
                {
                    BackgroundForm.UseImmersiveDarkMode(viewer.Handle, darkMode);
                    viewer.Fdesktop = this;
                    viewer.ImageList = imageList;
                    viewer.ImageIndex = SelectedIndices.First();
                    viewer.DeleteCallback = DeleteThumbnails;
                    viewer.SelectCallback = i =>
                    {
                        if (SelectedIndices.Count() <= 1)
                        {
                            SelectedIndices = new[] { i };
                            thumbnailList1.Items[i].EnsureVisible();
                        }
                    };
                    viewer.ShowDialog();
                }
            }
        }

        private void ShowProfilesForm()
        {
            var form = FormFactory.Create<FProfiles>();
            BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
            form.ImageCallback = ReceiveScannedImage(true);
            form.ShowDialog();
            RunAutoBarcodeDetection();
            updateProfileButton();
        }

        private void ResetImage()
        {
            if (SelectedIndices.Any())
            {
                if (MessageBox.Show(string.Format(MiscResources.ConfirmResetImages, SelectedIndices.Count()), MiscResources.ResetImage, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    imageList.ResetTransforms(SelectedIndices);
                    changeTracker.Made();
                }
            }
        }

        #endregion

        #region Actions - Save/Email/Import

        private async void SavePDF(List<ScannedImage> images, bool all = true)
        {
            documents = thumbnailList1.GroupRefresh(imageList.Images);
            //Save the range of page from the first document
            //Need to be able to do better range that this. This is risky.
            if (all & documents.Count>1)
            {
                //Refresh the list and group to be sure the document count is ok
                documents = thumbnailList1.GroupRefresh(imageList.Images);
                //if (documents.Count==0)
                //    await exportHelper.SavePDF(images, notify, 0);

                for (int a = 0; a < documents.Count; a++) 
                {
                    bool result = await exportHelper.SavePDF(images.GetRange(documents[a].firstpage, (documents[a].lastpage) - documents[a].firstpage), notify, a);
                    if (result)
                    {
                        changeTracker.Made();
                        if (appConfigManager.Config.DeleteAfterSaving)
                        {
                            SafeInvoke(() =>
                            {
                                imageList.Delete(imageList.Images.IndiciesOf(images));
                                DeleteThumbnails();
                            });
                        }
                    }
                    else break;
                }
            } else
            {
                bool result = await exportHelper.SavePDF(images, notify, 0);
                if (result)
                {
                    changeTracker.Made();
                    if (appConfigManager.Config.DeleteAfterSaving)
                    {
                        SafeInvoke(() =>
                        {
                            imageList.Delete(imageList.Images.IndiciesOf(images));
                            DeleteThumbnails();
                        });
                    }
                }

            }

        }
        // For exporting. Only with projects
        private async void SaveProjectImages(List<ScannedImage> images, bool bypassprompt = false)
        {
            ImageSettingsContainer.ProjectSettings.BatchName = projectName;
            if (await exportHelper.SaveProjectImages(images, notify, bypassprompt))
            {

                changeTracker.Made();
                if (appConfigManager.Config.DeleteAfterSaving)
                {
                    imageList.Delete(imageList.Images.IndiciesOf(images));
                    DeleteThumbnails();
                }
            }
        }
        private async void SaveImages(List<ScannedImage> images, bool bypassprompt = false)
        {
            ImageSettingsContainer.ProjectSettings.BatchName = projectName;
            if (await exportHelper.SaveImages(images, notify, bypassprompt))
            {
                
                changeTracker.Made();
                if (appConfigManager.Config.DeleteAfterSaving)
                {
                    imageList.Delete(imageList.Images.IndiciesOf(images));
                    DeleteThumbnails();
                }
            }
        }

        private void Import()
        {
            var ofd = new OpenFileDialog
            {
                Multiselect = true,
                CheckFileExists = true,
                Filter = MiscResources.FileTypeAllFiles + @"|*.*|" +
                         MiscResources.FileTypePdf + @"|*.pdf|" +
                         MiscResources.FileTypeImageFiles + @"|*.bmp;*.emf;*.exif;*.gif;*.jpg;*.jpeg;*.png;*.tiff;*.tif|" +
                         MiscResources.FileTypeBmp + @"|*.bmp|" +
                         MiscResources.FileTypeEmf + @"|*.emf|" +
                         MiscResources.FileTypeExif + @"|*.exif|" +
                         MiscResources.FileTypeGif + @"|*.gif|" +
                         MiscResources.FileTypeJpeg + @"|*.jpg;*.jpeg|" +
                         MiscResources.FileTypePng + @"|*.png|" +
                         MiscResources.FileTypeTiff + @"|*.tiff;*.tif"
            };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                ImportFilesAfterSelection(ofd.FileNames);
            }
        }

        private void ImportFiles(IEnumerable<string> files)
        {
            //remove the selection while importing files
            //SelectedIndices = Enumerable.Range(0, 0);
            var op = operationFactory.Create<ImportOperation>();
            if (op.Start(OrderFiles(files), ReceiveScannedImage()))
            {
                operationProgress.ShowProgress(op);
            }
            thumbnailList1.GroupRefresh(imageList.Images);
            
        }

        // Without a selection, imported files are appended to the end.
        // With a selection, they are inserted right after the last selected image instead of replacing it.
        private void ImportFilesAfterSelection(IEnumerable<string> files)
        {
            var selected = SelectedIndices.ToList();
            if (!selected.Any())
            {
                ImportFiles(files);
                return;
            }

            int nextIndex = selected.Max() + 1;
            var op = operationFactory.Create<ImportOperation>();
            if (op.Start(OrderFiles(files), img =>
            {
                int index = nextIndex++;
                BeginInvoke((Action)(() => InsertImageAt(img, index)));
            }))
            {
                operationProgress.ShowProgress(op);
            }
        }

        private List<string> OrderFiles(IEnumerable<string> files)
        {
            // Custom ordering to account for numbers so that e.g. "10" comes after "2"
            var filesList = files.ToList();
            filesList.Sort(new NaturalStringComparer());
            return filesList;
        }

        private void ImportDirect(DirectImageTransfer data, bool copy)
        {
           var op = operationFactory.Create<DirectImportOperation>();
           if (op.Start(data, copy, ReceiveScannedImage()))
           {
                operationProgress.ShowProgress(op);
           }
            
        }

        #endregion

        #region Keyboard Shortcuts

        private void AssignKeyboardShortcuts()
        {
            // Defaults
            
            ksm.Assign("Enter", tsScan);
            ksm.Assign("Ctrl+B", tsBatchScan);
            ksm.Assign("Ctrl+O", tsImport);
            ksm.Assign("Ctrl+S", tsdSavePDF);
            ksm.Assign("Ctrl+P", printToolStripMenuItem);
            ksm.Assign("Ctrl+L", loadProjectTool_TSMI);
            ksm.Assign("Ctrl+OemMinus", btnZoomOut);
            ksm.Assign("Ctrl+Oemplus", btnZoomIn);
            ksm.Assign("Del", Delete);
            ksm.Assign("Ctrl+A", ctxSelectAll);
            ksm.Assign("Ctrl+C", ctxCopy);
            ksm.Assign("Ctrl+V", ctxPaste);
            ksm.Assign("Space", tsView);
            ksm.Assign("pgdn", PageDown);
            ksm.Assign("pgup", PageUp);
            ksm.Assign("Ctrl+Numpad1", tsRotateLeft);
            ksm.Assign("Ctrl+NumPad2", tsFlip);
            ksm.Assign("Ctrl+Numpad3", tsRotateRight);
            ksm.Assign("Ctrl+Down", tsCustomRotation);
            ksm.Assign("C", tsCrop);
            ksm.Assign("M", tsMask);
            ksm.Assign("B", tsBrightnessContrast);
            ksm.Assign("H", tsHueSaturation);
            ksm.Assign("D", tsDeskew);
            ksm.Assign("Q", PrecacheOcr);
            //ksm.Assign("Ctrl+Up", MoveUp);
            //ksm.Assign("Ctrl+Left", MoveUp);
            //ksm.Assign("Ctrl+Down", MoveDown);
            //ksm.Assign("Ctrl+Right", MoveDown);
            //ksm.Assign("Ctrl+Shift+Del", tsClear);

            // Configured

            var ks = userConfigManager.Config.KeyboardShortcuts ?? appConfigManager.Config.KeyboardShortcuts ?? new KeyboardShortcuts();

            //ksm.Assign(ks.About, tsAbout);
            ksm.Assign(ks.BatchScan, tsBatchScan);
            //ksm.Assign(ks.Clear, tsClear);
            ksm.Assign(ks.Delete, tsDelete);
            ksm.Assign(ks.ImageBlackWhite, tsBlackWhite);
            ksm.Assign(ks.ImageBrightness, tsBrightnessContrast);
            ksm.Assign(ks.ImageContrast, tsBrightnessContrast);
            ksm.Assign(ks.ImageCrop, tsCrop);
            ksm.Assign(ks.ImageMask, tsMask);
            ksm.Assign(ks.ImageHue, tsHueSaturation);
            ksm.Assign(ks.ImageSaturation, tsHueSaturation);
            ksm.Assign(ks.ImageSharpen, tsSharpen);
            ksm.Assign(ks.ImageReset, tsReset);
            ksm.Assign(ks.ImageView, tsView);
            ksm.Assign(ks.Import, tsImport);
            //ksm.Assign(ks.MoveDown, MoveDown); // TODO
            //ksm.Assign(ks.MoveUp, MoveUp); // TODO
            ksm.Assign(ks.NewProfile, tsNewProfile);
            //ksm.Assign(ks.Ocr, tsOcr);
            ksm.Assign(ks.Print, printToolStripMenuItem);
            ksm.Assign(ks.Profiles, ShowProfilesForm);

            ksm.Assign(ks.ReorderAltDeinterleave, tsAltDeinterleave);
            ksm.Assign(ks.ReorderAltInterleave, tsAltInterleave);
            ksm.Assign(ks.ReorderDeinterleave, tsDeinterleave);
            ksm.Assign(ks.ReorderInterleave, tsInterleave);
            ksm.Assign(ks.ReorderReverseAll, tsReverseAll);
            ksm.Assign(ks.ReorderReverseSelected, tsReverseSelected);
            ksm.Assign(ks.RotateCustom, tsCustomRotation);
            ksm.Assign(ks.RotateFlip, tsFlip);
            ksm.Assign(ks.RotateLeft, tsRotateLeft);
            ksm.Assign(ks.RotateRight, tsRotateRight);
            ksm.Assign(ks.RotateDeskew, tsDeskew);
            ksm.Assign(ks.SaveImages, tsdSaveImages);
            ksm.Assign(ks.SaveImagesAll, tsSaveImagesAll);
            ksm.Assign(ks.SaveImagesSelected, tsSaveImagesSelected);
            ksm.Assign(ks.SavePDF, tsdSavePDF);
            ksm.Assign(ks.SavePDFAll, tsSavePDFAll);
            ksm.Assign(ks.SavePDFSelected, tsSavePDFSelected);
            ksm.Assign(ks.ScanDefault, tsScan);

            ksm.Assign(ks.ZoomIn, btnZoomIn);
            ksm.Assign(ks.ZoomOut, btnZoomOut);
        }

        private void AssignProfileShortcut(int i, ToolStripMenuItem item)
        {
            var sh = GetProfileShortcut(i);
            if (string.IsNullOrWhiteSpace(sh) && i <= 11)
            {
                sh = "F" + (i+1);
            }
            ksm.Assign(sh, item);
        }

        private string GetProfileShortcut(int i)
        {
            var ks = userConfigManager.Config.KeyboardShortcuts ?? appConfigManager.Config.KeyboardShortcuts ?? new KeyboardShortcuts();
            switch (i)
            {
                case 1:
                    return ks.ScanProfile1;
                case 2:
                    return ks.ScanProfile2;
                case 3:
                    return ks.ScanProfile3;
                case 4:
                    return ks.ScanProfile4;
                case 5:
                    return ks.ScanProfile5;
                case 6:
                    return ks.ScanProfile6;
                case 7:
                    return ks.ScanProfile7;
                case 8:
                    return ks.ScanProfile8;
                case 9:
                    return ks.ScanProfile9;
                case 10:
                    return ks.ScanProfile10;
                case 11:
                    return ks.ScanProfile11;
                case 12:
                    return ks.ScanProfile12;
            }
            return null;
        }

        // Starts OCR for all pages and caches the results (toolbar button, keyboard shortcut Q)
        private async void PrecacheOcr() 
        {
            if (ocrPrecacheRunning || !imageList.Images.Any())
            {
                return;
            }
            if (!ocrManager.IsReady)
            {
                statusStrip1.Items[0].Text = "OCR non prêt : installez le moteur OCR (menu OCR)";
                return;
            }

            ocrPrecacheRunning = true;
            ocrCancelRequested = false;
            UpdateOcrButtons();
            var images = imageList.Images.ToList();
            var param = new ScanParams();
            try
            {
                for (int i = 0; i < images.Count && !ocrCancelRequested; i++)
                {
                    try
                    {
                        using (var rendered = await scannedImageRenderer.Render(images[i]))
                        {
                            if (ocrCancelRequested)
                            {
                                break;
                            }
                            string tempPath = scannedImageHelper.SaveForBackgroundOcr(rendered, param, true);
                            scannedImageHelper.RunBackgroundOcr(images[i], param, tempPath, true);
                        }
                    }
                    catch (ObjectDisposedException e)
                    {
                        // The page was deleted while precaching: skip it
                        Log.ErrorException("OCR precache skipped a disposed page", e);
                    }
                    statusStrip1.Items[0].Text = $"OCR en file : page {i + 1} / {images.Count}";
                }
                statusStrip1.Items[0].Text = ocrCancelRequested ? "OCR annulé" : "OCR mis en file pour toutes les pages";
            }
            catch (Exception e)
            {
                Log.ErrorException("Error during OCR precache", e);
                statusStrip1.Items[0].Text = "Erreur pendant le pré-calcul OCR";
            }
            finally
            {
                ocrPrecacheRunning = false;
                UpdateOcrButtons();
            }
            
            //Test Image Magic
            //ScannedImageHelper.TestImageMagick(imageList.Images[SelectedIndices.First()]);
        }

        private void InitOcrPrecacheButtons()
        {
            // Created in code so they can be removed easily (delete this method and its call in the constructor)
            btnOcrPrecache = new ToolStripButton
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = "Pré-calcul OCR",
                ToolTipText = "Lance l'OCR de toutes les pages et le met en cache (raccourci : Q)",
                Enabled = false
            };
            btnOcrPrecache.Click += (sender, e) => PrecacheOcr();

            btnOcrCancel = new ToolStripButton
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = "Annuler OCR",
                ToolTipText = "Annule les calculs OCR en attente",
                Visible = false
            };
            btnOcrCancel.Click += (sender, e) => CancelOcrPrecache();

            tStrip.Items.Add(btnOcrPrecache);
            tStrip.Items.Add(btnOcrCancel);
            scannedImageHelper.OcrBusyChanged += OcrBusyChangedHandler;
        }

        private void CancelOcrPrecache()
        {
            ocrCancelRequested = true;
            scannedImageHelper.CancelBackgroundOcr();
            UpdateOcrButtons();
        }

        private void OcrBusyChangedHandler(object sender, EventArgs e)
        {
            SafeInvokeAsync(UpdateOcrButtons);
        }

        private void UpdateOcrButtons()
        {
            if (btnOcrPrecache == null || btnOcrCancel == null)
            {
                return;
            }
            bool busy = ocrPrecacheRunning || scannedImageHelper.IsOcrBusy;
            btnOcrCancel.Visible = busy;
            btnOcrPrecache.Enabled = imageList.Images.Any() && !busy;
        }

        private void PageDown()
        {
            int count = SelectedIndices.First() + 1;
            if (SelectedIndices.Count() > 0 && count < thumbnailList1.Items.Count)
            {
                thumbnailList1.Items[count].Selected = true;
                thumbnailList1.Items[count - 1].Selected = false;
                thumbnailList1.EnsureVisible(thumbnailList1.Items[count].Index);
            }
        }
        private void PageUp()
        {
            int count = SelectedIndices.First() - 1;
            if (SelectedIndices.Count() > 0)
            {
                if (count > -1)
                {
                    thumbnailList1.Items[count].Selected = true;
                    thumbnailList1.Items[count + 1].Selected = false;
                    thumbnailList1.EnsureVisible(thumbnailList1.Items[count].Index);
                }
            }
        }
        private void ThumbnailList1_KeyDown(object sender, KeyEventArgs e)
        {
            ksm.Perform(e);
        }

        private void ThumbnailList1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (ModifierKeys.HasFlag(Keys.Control))
            {
                StepThumbnailSize(e.Delta / (double)SystemInformation.MouseWheelScrollDelta);
            }
        }

        #endregion

        #region Event Handlers - Misc

        // New feature. Will update the position of splitter bar relatively by a ratio when the application window size is changed.
        private void App_SizeChanged(object sender, System.EventArgs e)
        {
            Control control = (Control)sender;
            if (Oldsize!=Size.Empty && WindowState != FormWindowState.Minimized)
            {        
                    float ratio = (float)control.Size.Width / (float)Oldsize.Width;
                    splitContainer1.SplitterDistance = (int)(splitContainer1.SplitterDistance * ratio);
                    Oldsize.Width = control.Size.Width;
                    appConfigManager.Save();          
            }
        }

        private void ThumbnailList1_ItemActivate(object sender, EventArgs e)
        {
            PreviewImage();
        }


        private void DisplaySelectedItem_info(bool minimum = false)
        {
            // The selection is read once. Each SelectedIndices.Count()/First() enumerated the whole selection
            // through the native ListView, a dozen times per call.
            int selectedCount = thumbnailList1.SelectedIndices.Count;
            int imageCount = imageList.Images.Count;

            if (selectedCount < 1 || selectedCount > imageCount)
            {
                statusStrip1.Items[0].Text = MiscResources.No_selection;
                UpdateToolbar();
                return;
            }

            if (selectedCount > 1)
            {
                statusStrip1.Items[0].Text = MiscResources.Selection + selectedCount.ToString();
                UpdateToolbar();
                return;
            }

            //If the index is invalid
            int selected = thumbnailList1.SelectedIndices[0];
            if (selected < 0 || selected > imageCount - 1)
            {
                statusStrip1.Items[0].Text = MiscResources.No_selection;
                UpdateToolbar();
                return;
            }

            var image = imageList.Images[selected];
            String text = (selected + 1).ToString();
            String text2 = image.infoResolution;
            String text3 = image.BarCodeData;

            String text4 = "";
            if (text3 != "" && text3 != null)
                text4 = "Barcode: " + text3;
            String format = image.infoFormat;
            string side;
            if (image.SheetSide == 1)
                side = "- side: front";
            else if (image.SheetSide == 2)
                side = "- side: back";
            else
                side = "- side: front only";

            statusStrip1.Items[0].Text = "Document(s): " + thumbnailList1.GetGroups().Count.ToString() + " | Image(s): " + text + "/" + imageCount + " : Size: " + text2 + " - " + text4 + " - " + format + side;

            if (!minimum)
            {
                GetPreviewImage(image, true);
                UpdateToolbar();
            }
            //Put the barcode data in the toolbar
            TS_BarcodeInfo.TextBox.Text = text3;
        }

        // True while SelectedIndices is being set: the per-item notifications are ignored, the setter updates once
        private bool selectionChanging;
        // True while a selection update is waiting to run (see ThumbnailList1_SelectedIndexChanged)
        private bool selectionInfoPending;

        // CC - Should display the status of the selected thumbnail (CODEBAR PRESENT, SIZE, ETC, in the status bar)
        // The ListView raises this event once per item whose selection changes (Ctrl+A on 1000 pages: 1000 times).
        // The update is posted once and runs after the whole change, instead of once per item.
        private void ThumbnailList1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (selectionChanging || selectionInfoPending)
            {
                return;
            }
            selectionInfoPending = true;
            SafeInvokeAsync(() =>
            {
                selectionInfoPending = false;
                DisplaySelectedItem_info();
            });
        }

        private void thumbnailList1_MouseMove(object sender, MouseEventArgs e)
        {
            Cursor = thumbnailList1.GetItemAt(e.X, e.Y) == null ? Cursors.Default : Cursors.Hand;
        }

        private void thumbnailList1_MouseLeave(object sender, EventArgs e)
        {
            Cursor = Cursors.Default;
        }

        private void tStrip_DockChanged(object sender, EventArgs e)
        {
            RelayoutToolbar();
        }

        #endregion

        #region Event Handlers - Toolbar

        private async void tsScan_ButtonClick(object sender, EventArgs e)
        {
            await ScanDefault();
            changeTracker.Made();
        }

        private async void tsNewProfile_Click_1(object sender, EventArgs e)
        {
            await ScanWithNewProfile();
        }

        private async void tsInsert_Click(object sender, EventArgs e)
        {
            this.insert = true; // enable insert mode
            await ScanDefault();
            changeTracker.Made();
            this.insert = false;
        }

        private void tsBatchScan_Click(object sender, EventArgs e)
        {
            var form = FormFactory.Create<FBatchScan>();
            BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
            form.ImageCallback = ReceiveScannedImage(true);
            form.ShowDialog();
            RunAutoBarcodeDetection();
            updateProfileButton();
        }

        private void tsOCR_Click(object sender, EventArgs e)
        {
            if (appConfigManager.Config.HideOcrButton)
            {
                return;
            }

            if (ocrManager.MustUpgrade && !appConfigManager.Config.NoUpdatePrompt)
            {
                // Re-download a fixed version on Windows XP if needed
                MessageBox.Show(MiscResources.OcrUpdateAvailable, "", MessageBoxButtons.OK, MessageBoxIcon.Information);
                var progressForm = FormFactory.Create<FDownloadProgress>();
                BackgroundForm.UseImmersiveDarkMode(progressForm.Handle, darkMode);
                progressForm.QueueFile(ocrManager.EngineToInstall.Component);
                progressForm.ShowDialog();
            }

            if (ocrManager.MustInstallPackage)
            {
                const string packages = "\ntesseract-ocr";
                MessageBox.Show(MiscResources.TesseractNotAvailable + packages, MiscResources.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (ocrManager.IsReady)
            {
                if (ocrManager.CanUpgrade && !appConfigManager.Config.NoUpdatePrompt)
                {
                    MessageBox.Show(MiscResources.OcrUpdateAvailable, "", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    FormFactory.Create<FOcrLanguageDownload>().ShowDialog();
                }
                FormFactory.Create<FOcrSetup>().ShowDialog();
            }
            else
            {
                FormFactory.Create<FOcrLanguageDownload>().ShowDialog();
                if (ocrManager.IsReady)
                {
                    FormFactory.Create<FOcrSetup>().ShowDialog();
                }
            }

        }
        private void updateProfileButton()
        {
            const int staticButtonCount = 3;

            // Clean up the dropdown
            while (tsCombo_Profiles.DropDownItems.Count > staticButtonCount)
            {
                tsCombo_Profiles.DropDownItems.RemoveAt(0);
            }

            //there is no profile at all.
            if (profileManager.Profiles.Count == 0)
                return;

            // Populate the dropdown
            var defaultProfile = profileManager.DefaultProfile;
            tsCombo_Profiles.Text = defaultProfile.DisplayName;
            tsCombo_Profiles.Image = Icons.scanner_48;


            int i = 1;
            foreach (var profile in profileManager.Profiles)
            {
                var item = new ToolStripMenuItem
                {
                    Text = profile.DisplayName.Replace("&", "&&"),
                    Image = profile == defaultProfile ? Icons.accept : null,
                    ImageScaling = ToolStripItemImageScaling.None
                };
                AssignProfileShortcut(i, item);
                item.Click += (sender, args) =>
                {
                    profileManager.DefaultProfile = profile;
                    profileManager.Save();

                    updateProfileButton();

                    //By default it should not perform a scan when pressing the profile key. Investigating an alternate way to trigger this like CTRL+PROFILE KEY
                    //await scanPerformer.PerformScan(profile, new ScanParams(), this, notify, ReceiveScannedImage());
                    //Activate();
                };
                tsCombo_Profiles.DropDownItems.Insert(tsCombo_Profiles.DropDownItems.Count - staticButtonCount, item);

                i++;
            }
        }
        private void tsProfiles_Click(object sender, EventArgs e)
        {
            ShowProfilesForm();
        }

        private void tsImport_Click_1(object sender, EventArgs e)
       {
            if (appConfigManager.Config.HideImportButton)
            {
                return;
            }
            Import();
        }

        private void tsdSavePDF_ButtonClick(object sender, EventArgs e)
        {
            if (appConfigManager.Config.HideSavePdfButton)
            {
                return;
            }

            var action = appConfigManager.Config.SaveButtonDefaultAction;

            if (action == SaveButtonDefaultAction.AlwaysPrompt
                || action == SaveButtonDefaultAction.PromptIfSelected && SelectedIndices.Any())
            {
                tsdSavePDF.ShowDropDown();
            }
            else if (action == SaveButtonDefaultAction.SaveSelected && SelectedIndices.Any())
            {
                SavePDF(SelectedImages.ToList(),false);
            }
            else
            {
                SavePDF(imageList.Images);
            }
        }

        private void tsdSaveImages_ButtonClick(object sender, EventArgs e)
        {
            if (appConfigManager.Config.HideSaveImagesButton)
            {
                return;
            }

            var action = appConfigManager.Config.SaveButtonDefaultAction;

            if (action == SaveButtonDefaultAction.AlwaysPrompt
                || action == SaveButtonDefaultAction.PromptIfSelected && SelectedIndices.Any())
            {
                tsdSaveImages.ShowDropDown();
            }
            else if (action == SaveButtonDefaultAction.SaveSelected && SelectedIndices.Any())
            {
                SaveImages(SelectedImages.ToList());
            }
            else
            {
                SaveImages(imageList.Images);
            }
        }

        private async void printToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (appConfigManager.Config.HidePrintButton)
            {
                return;
            }

            var changeToken = changeTracker.State;
            if (await scannedImagePrinter.PromptToPrint(imageList.Images, SelectedImages.ToList()))
            {
                changeTracker.Saved(changeToken);
            }
        }

        private void tsMove_FirstClick(object sender, EventArgs e)
        {
            MoveUp();
        }

        private void tsMove_SecondClick(object sender, EventArgs e)
        {
            MoveDown();
        }

        private void tsDelete_Click(object sender, EventArgs e)
        {
            Delete();
        }

        private void tsAbout_Click(object sender, EventArgs e)
        {
            var form = FormFactory.Create<FAbout>().ShowDialog();
        }
     #endregion

        #region Event Handlers - Save/Email Menus

         private void tsdSavePDF_Click(object sender, EventArgs e)
        {
        }

        private void tsSavePDFAll_Click(object sender, EventArgs e)
        {

            if (appConfigManager.Config.HideSavePdfButton)
            {
                return;
            }

            SavePDF(imageList.Images);
        }

        private void tsSavePDFSelected_Click_1(object sender, EventArgs e)
        {
            if (appConfigManager.Config.HideSavePdfButton)
            {
                return;
            }

            SavePDF(SelectedImages.ToList(),false);
        }

        private void tsPDFSettings_Click_1(object sender, EventArgs e)
        {
            var form = FormFactory.Create<FPdfSettings>().ShowDialog();
        }

        private void tsSaveImagesAll_Click(object sender, EventArgs e)
        {
            
        }

        private void tsSaveImagesAll_Click_1(object sender, EventArgs e)
        {
            if (appConfigManager.Config.HideSaveImagesButton)
            {
                return;
            }

            SaveImages(imageList.Images);

        }
        private void tsSaveImagesSelected_Click_1(object sender, EventArgs e)
        {
            if (appConfigManager.Config.HideSaveImagesButton)
            {
                return;
            }
            SaveImages(SelectedImages.ToList());
        }

        private void tsImageSettings_Click_1(object sender, EventArgs e)
        {
            var form = FormFactory.Create<FImageSettings>().ShowDialog();
        }

        private void tsPdfSettings2_Click(object sender, EventArgs e)
        {
            var form = FormFactory.Create<FPdfSettings>().ShowDialog();
        }

        private void tsVectorPdfDpi_Click(object sender, EventArgs e)
        {
            FormFactory.Create<FVectorPdfDpi>().ShowDialog();
        }

        #endregion

        #region Event Handlers - Image Menu

        private void tsView_Click(object sender, EventArgs e)
        {
            PreviewImage();
        }

        private void tsMask_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FMask>();
                BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);

                form.Image = SelectedImages.First();
                form.MaximizeBox = true;
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateToolbar();
            }
        }

        private void tsCrop_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FCrop>();
                BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);

                form.Image = SelectedImages.First();
                form.MaximizeBox = true;
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateToolbar();
            }
        }

        private void tsBrightnessContrast_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FBrightnessContrast>();
                BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
                form.MaximizeBox = true;
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateToolbar();
            }
        }

        private void tsHueSaturation_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FHueSaturation>();
                BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
                form.MaximizeBox = true;
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateToolbar();
            }
        }

        private void tsBlackWhite_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FBlackWhite>();
                BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
                form.MaximizeBox = true;
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateToolbar();
            }
        }

        private void tsSharpen_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FSharpen>();
                BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
                form.MaximizeBox = true;
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
            }
        }

        private void tsReset_Click(object sender, EventArgs e)
        {
            ResetImage();
        }

        #endregion

        #region Event Handlers - Rotate Menu

        private async void tsRotateLeft_Click(object sender, EventArgs e)
        {
            await RotateLeft();
        }

        private async void tsRotateRight_Click(object sender, EventArgs e)
        {
            await RotateRight();
        }

        private async void tsFlip_Click(object sender, EventArgs e)
        {
            await Flip();
        }

        private void tsDeskew_Click(object sender, EventArgs e)
        {
            Deskew();
        }

        private void tsCustomRotation_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FRotate>();
                BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
                form.MaximizeBox = true;
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateThumbnails(SelectedIndices.ToList(), true, false);
            }
        }

        #endregion

        #region Event Handlers - Reorder Menu

        private void tsInterleave_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 3)
            {
                return;
            }
            UpdateThumbnails(imageList.Interleave(SelectedIndices), true, false);
            changeTracker.Made();
        }

        private void tsDeinterleave_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 3)
            {
                return;
            }
            UpdateThumbnails(imageList.Deinterleave(SelectedIndices), true, false);
            changeTracker.Made();
        }

        private void tsAltInterleave_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 3)
            {
                return;
            }
            UpdateThumbnails(imageList.AltInterleave(SelectedIndices), true, false);
            changeTracker.Made();
        }

        private void tsAltDeinterleave_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 3)
            {
                return;
            }
            UpdateThumbnails(imageList.AltDeinterleave(SelectedIndices), true, false);
            changeTracker.Made();
        }

        private void tsReverseAll_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 2)
            {
                return;
            }
            UpdateThumbnails(imageList.Reverse(), true, false);
            changeTracker.Made();
        }

        private void tsReverseSelected_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Count() < 2)
            {
                return;
            }
            UpdateThumbnails(imageList.Reverse(SelectedIndices), true, true);
            changeTracker.Made();
        }

        #endregion

        #region Context Menu

        private void contextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            
            if (!SelectedIndices.Any())
            {
               ctxScanBarCode.Enabled = false;
            }
            else
            {
                ctxScanBarCode.Enabled = true;
            }

            if (Clipboard.ContainsImage() || (Clipboard.GetDataObject()!=null))
            {
                ctxPaste.Enabled = true;
            }
            else
            {
                ctxPaste.Enabled = false;
            }
            
            // Always visible. The click handler checks the selection and the copied images, and shows a message if needed.
            ctxPasteAfter.Visible = true;
            ctxPasteAfter.Enabled = true;

            /*if (!imageList.Images.Any() && !ctxPaste.Enabled)
            {
                e.Cancel = true;
            }*/
        }

        private void importToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Import();
        }

        private void ctxSelectAll_Click(object sender, EventArgs e)
        {
            SelectAll();
        }

        private void ctxView_Click(object sender, EventArgs e)
        {
            PreviewImage();
        }

        private void ctxCopy_Click(object sender, EventArgs e)
        {
            CopyImages();
        }

        private void ctxPaste_Click(object sender, EventArgs e)
        {
            PasteImages();
        }

        private void ctxPasteAfter_Click(object sender, EventArgs e)
        {
            PasteImagesAfterSelection();
        }

        private void ctxDelete_Click(object sender, EventArgs e)
        {
            Delete();
        }

        #endregion

        #region Clipboard

        private async void CopyImages()
        {
            if (SelectedIndices.Any())
            {
                copiedImages = new DirectImageTransfer(SelectedImages);
                // TODO: Make copy an operation
                var ido = await GetDataObjectForImages(SelectedImages, true);
                Clipboard.SetDataObject(ido);

                //this should copy the image to the clipboard
                Bitmap bit = await scannedImageRenderer.Render(SelectedImages.First());
                bitmap = new Bitmap(bit);
                Clipboard.SetImage((Bitmap)bit);
                bit.Dispose();
            }
        }

        private void PasteImages()
        {
            var ido = Clipboard.GetDataObject();

            // Check if the clipboard contains a bitmap but no data
            if (Clipboard.ContainsImage())
            {
                ScannedImage img1 = new ScannedImage((Bitmap)Clipboard.GetImage(),ScanBitDepth.C24Bit,true,100);
                imageList.Images.Add(img1);
                img1.ThumbnailChanged += ImageThumbnailChanged;
                img1.ThumbnailInvalidated += ImageThumbnailInvalidated;
                
               
                AddThumbnails();
                renderThumbnailsWaitHandle.Set();
                return;

            }

            // Check if the clipboard does not contains a bitmap or any data
            if (!CanPaste)
            {
                return;
            }

            // Check if the clipboard contains a DirectImageTransfer object
            if (ido.GetDataPresent(typeof(DirectImageTransfer).FullName))
            {
                var data = (DirectImageTransfer)ido.GetData(typeof(DirectImageTransfer).FullName);
                ImportDirect(data, true);
            }
        }

        private void PasteImagesAfterSelection()
        {
            if (SelectedIndices.Count() != 1)
            {
                MessageBox.Show("Sélectionnez une seule image, puis choisissez « Coller après cette image ».",
                    title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (copiedImages == null || copiedImages.ImageRecovery.Length == 0)
            {
                MessageBox.Show("Aucune image copiée. Copiez d'abord une ou plusieurs images.",
                    title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Insert right after the selected image. Copied images are inserted one after another from that point.
            int nextIndex = SelectedIndices.First() + 1;
            var op = operationFactory.Create<DirectImportOperation>();
            if (op.Start(copiedImages, true, img =>
            {
                int index = nextIndex++;
                BeginInvoke((Action)(() => InsertImageAt(img, index)));
            }))
            {
                operationProgress.ShowProgress(op);
            }
        }

        // Runs on the UI thread. Inserts one image into the list at the given index.
        private void InsertImageAt(ScannedImage img, int index)
        {
            lock (imageList)
            {
                index = Math.Min(index, imageList.Images.Count);
                imageList.Images.Insert(index, img);
                img.MovedTo(index);
                img.ThumbnailChanged += ImageThumbnailChanged;
                img.ThumbnailInvalidated += ImageThumbnailInvalidated;
            }
            AddThumbnails();
            UpdateToolbar();
            changeTracker.Made();
            renderThumbnailsWaitHandle.Set();
        }

        private bool CanPaste
        {
            get
            {
                var ido = Clipboard.GetDataObject();
                return ido != null && ido.GetDataPresent(typeof(DirectImageTransfer).FullName) && Clipboard.ContainsImage();
            }
        }

        private async Task<IDataObject> GetDataObjectForImages(IEnumerable<ScannedImage> images, bool includeBitmap)
        {
            var imageList = images.ToList();
            IDataObject ido = new DataObject();
            if (imageList.Count == 0)
            {
                return ido;
            }
            if (includeBitmap)
            {
                using (var firstBitmap = await scannedImageRenderer.Render(imageList[0]))
                {
                    ido.SetData(DataFormats.Bitmap, true, new Bitmap(firstBitmap));
                    ido.SetData(DataFormats.Rtf, true, await RtfEncodeImages(firstBitmap, imageList));
                }
            }
            ido.SetData(typeof(DirectImageTransfer), new DirectImageTransfer(imageList));
            return ido;
        }

        private async Task<string> RtfEncodeImages(Bitmap firstBitmap, List<ScannedImage> images)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            if (!AppendRtfEncodedImage(firstBitmap, images[0].FileFormat, sb, false))
            {
                return null;
            }
            foreach (var img in images.Skip(1))
            {
                using (var bitmap = await scannedImageRenderer.Render(img))
                {
                    if (!AppendRtfEncodedImage(bitmap, img.FileFormat, sb, true))
                    {
                        break;
                    }
                }
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static bool AppendRtfEncodedImage(System.Drawing.Image image, ImageFormat format, StringBuilder sb, bool par)
        {
            const int maxRtfSize = 20 * 1000 * 1000;
            using (var stream = new MemoryStream())
            {
                image.Save(stream, format);
                if (sb.Length + stream.Length * 2 > maxRtfSize)
                {
                    return false;
                }

                if (par)
                {
                    sb.Append(@"\par");
                }
                sb.Append(@"{\pict\pngblip\picw");
                sb.Append(image.Width);
                sb.Append(@"\pich");
                sb.Append(image.Height);
                sb.Append(@"\picwgoa");
                sb.Append(image.Width);
                sb.Append(@"\pichgoa");
                sb.Append(image.Height);
                sb.Append(@"\hex ");
                // Do a "low-level" conversion to save on memory by avoiding intermediate representations
                stream.Seek(0, SeekOrigin.Begin);
                int value;
                while ((value = stream.ReadByte()) != -1)
                {
                    int hi = value / 16, lo = value % 16;
                    sb.Append(GetHexChar(hi));
                    sb.Append(GetHexChar(lo));
                }
                sb.Append("}");
            }
            return true;
        }

        private static char GetHexChar(int n)
        {
            return (char)(n < 10 ? '0' + n : 'A' + (n - 10));
        }

        #endregion

        #region Thumbnail Resizing

        private void StepThumbnailSize(double step)
        {
            int thumbnailSize = UserConfigManager.Config.ThumbnailSize;
            thumbnailSize = (int)ThumbnailRenderer.StepNumberToSize(ThumbnailRenderer.SizeToStepNumber(thumbnailSize) + step);
            thumbnailSize = Math.Max(Math.Min(thumbnailSize, ThumbnailRenderer.MAX_SIZE), ThumbnailRenderer.MIN_SIZE);
            ResizeThumbnails(thumbnailSize);
        }

        private void ResizeThumbnails(int thumbnailSize)
        {
            if (!imageList.Images.Any())
            {
                // Can't show visual feedback so don't do anything
                return;
            }
            if (thumbnailList1.ThumbnailSize.Height == thumbnailSize)
            {
                // Same size so no resizing needed
                return;
            }

            // Save the new size to config
            UserConfigManager.Config.ThumbnailSize = thumbnailSize;
            UserConfigManager.Save();
            // Adjust the visible thumbnail display with the new size
            lock (thumbnailList1)
            {
                thumbnailList1.ThumbnailSize = new Size(thumbnailSize, thumbnailSize);
                var color = Color.Black;
                if (darkMode)
                    color = Color.Black;
                thumbnailList1.RegenerateThumbnailList(imageList.Images, color);

                if (thumbnailList1.Items.Count > 5 && !SelectedIndices.Any() && !recover)
                    thumbnailList1.EnsureVisible(thumbnailList1.Items.Count - 1);
            }

            SetThumbnailSpacing(thumbnailSize);
            UpdateToolbar();

            // Render high-quality thumbnails at the new size in a background task
            // The existing (poorly scaled) thumbnails are used in the meantime
            renderThumbnailsWaitHandle.Set();
        }

        private void SetThumbnailSpacing(int thumbnailSize)
        {
            thumbnailList1.Padding = new Padding(0, 20, 0, 0);
            const int MIN_PADDING = 6;
            const int MAX_PADDING = 66;
            // Linearly scale the padding with the thumbnail size
            int padding = MIN_PADDING + (MAX_PADDING - MIN_PADDING) * (thumbnailSize - ThumbnailRenderer.MIN_SIZE) / (ThumbnailRenderer.MAX_SIZE - ThumbnailRenderer.MIN_SIZE);
            int spacing = thumbnailSize + padding * 2;
            SetListSpacing(thumbnailList1, spacing, spacing);
        }

        private void SetListSpacing(ListView list, int hspacing, int vspacing)
        {
            const int LVM_FIRST = 0x1000;
            const int LVM_SETICONSPACING = LVM_FIRST + 53;
            Win32.SendMessage(list.Handle, LVM_SETICONSPACING, IntPtr.Zero, (IntPtr)(int)(((ushort)hspacing) | (uint)(vspacing << 16)));
        }

        // Indices of the thumbnails shown on screen, refreshed on the UI thread (see UpdateVisibleThumbnailRange).
        // Read by the render thread to render what the user sees first.
        private volatile int visibleThumbnailFirst;
        private volatile int visibleThumbnailLast = -1;

        // Pages whose thumbnail could not be rendered (e.g. unreadable file). They are skipped instead of blocking
        // the other pages, and retried when they change (ImageThumbnailInvalidated).
        private readonly HashSet<ScannedImage> failedThumbnails = new HashSet<ScannedImage>();

        private System.Windows.Forms.Timer visibleRangeTimer;

        private void StartThumbnailRendering()
        {
            // The visible range is cheap to compute (binary search) and the ListView has no scroll event:
            // a light timer keeps it current while the user scrolls. The same timer applies the new thumbnails
            // to the list in batches. Timer messages come after painting, so the list keeps redrawing.
            visibleRangeTimer = new System.Windows.Forms.Timer { Interval = 100 };
            visibleRangeTimer.Tick += (sender, args) =>
            {
                UpdateVisibleThumbnailRange();
                FlushThumbnailUpdates();
            };
            visibleRangeTimer.Start();
            new Thread(RenderThumbnails) { IsBackground = true, Name = "Thumbnail rendering" }.Start();
        }

        private void UpdateVisibleThumbnailRange()
        {
            if (closed)
            {
                return;
            }
            thumbnailList1.GetVisibleRange(out int first, out int last);
            if (first != visibleThumbnailFirst || last != visibleThumbnailLast)
            {
                visibleThumbnailFirst = first;
                visibleThumbnailLast = last;
                // Newly shown pages may be waiting for their thumbnail
                renderThumbnailsWaitHandle.Set();
            }
        }

        private void RenderThumbnails()
        {
            // Thumbnails are rendered in this process, on several threads. Pages are decoded directly at a reduced
            // size (ScaledImageDecoder), so each thread only needs a small amount of memory.
            int threads = Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 6));
            var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
            var fallback = new ExpFallback(100, 60 * 1000);
            while (!closed)
            {
                try
                {
                    List<ScannedImage> batch;
                    // Small batches: the priorities (visible pages first) are recomputed often, so scrolling is followed quickly
                    while (!closed && (batch = GetThumbnailsToRender(threads * 2, thumbnailList1.ThumbnailSize)).Count > 0)
                    {
                        var size = thumbnailList1.ThumbnailSize;
                        Parallel.ForEach(batch, options, img => RenderThumbnail(img, size));
                        fallback.Reset();
                    }
                }
                catch (Exception e)
                {
                    Log.ErrorException("Error rendering thumbnails", e);
                    Thread.Sleep(fallback.Value);
                    fallback.Increase();
                    continue;
                }
                renderThumbnailsWaitHandle.WaitOne();
            }
        }

        // Renders one thumbnail (or reads it from the disk cache). Runs on a render thread.
        private void RenderThumbnail(ScannedImage img, Size size)
        {
            try
            {
                if (!ThumbnailStillNeedsRendering(img, size))
                {
                    return;
                }
                using (var snapshot = img.Preserve())
                {
                    var thumb = ThumbnailCache.TryLoad(snapshot, size.Height);
                    if (thumb == null)
                    {
                        thumb = thumbnailRenderer.RenderThumbnailNow(snapshot, size.Height);
                        ThumbnailCache.Store(snapshot, size.Height, thumb);
                    }
                    if (!ThumbnailStillNeedsRendering(img, size))
                    {
                        thumb.Dispose();
                        return;
                    }
                    img.SetThumbnail(thumb, snapshot.TransformState);
                }
            }
            catch (ObjectDisposedException)
            {
                // The page was removed while its thumbnail was being rendered
            }
            catch (Exception e)
            {
                Log.ErrorException("Error rendering a thumbnail", e);
                lock (failedThumbnails)
                {
                    failedThumbnails.Add(img);
                }
            }
        }

        private bool ThumbnailStillNeedsRendering(ScannedImage next, Size size)
        {
            lock (next)
            {
                return !next.HasThumbnail || next.IsThumbnailDirty || next.CurrentThumbnailSize != size;
            }
        }

        /// <summary>
        /// Gets up to max pages that need a thumbnail, most important first:
        /// 1) pages on screen, 2) other pages without a thumbnail or with an outdated one (after the visible
        /// area first, then before it), 3) other pages whose thumbnail only has the wrong size.
        /// </summary>
        private List<ScannedImage> GetThumbnailsToRender(int max, Size size)
        {
            List<ScannedImage> listCopy;
            lock (imageList)
            {
                listCopy = imageList.Images.ToList();
            }
            HashSet<ScannedImage> failed;
            lock (failedThumbnails)
            {
                if (failedThumbnails.Count > 0)
                {
                    var present = new HashSet<ScannedImage>(listCopy);
                    failedThumbnails.RemoveWhere(x => !present.Contains(x));
                }
                failed = new HashSet<ScannedImage>(failedThumbnails);
            }

            int count = listCopy.Count;
            int first = Math.Max(0, Math.Min(visibleThumbnailFirst, count));
            int last = Math.Min(visibleThumbnailLast, count - 1);

            var visible = new List<ScannedImage>();
            var outdated = new List<ScannedImage>();
            var resized = new List<ScannedImage>();
            void Check(int i, bool isVisible)
            {
                var img = listCopy[i];
                if (failed.Contains(img))
                {
                    return;
                }
                bool missingOrDirty, wrongSize;
                lock (img)
                {
                    missingOrDirty = !img.HasThumbnail || img.IsThumbnailDirty;
                    wrongSize = img.CurrentThumbnailSize != size;
                }
                if (!missingOrDirty && !wrongSize)
                {
                    return;
                }
                if (isVisible)
                {
                    visible.Add(img);
                }
                else if (missingOrDirty)
                {
                    outdated.Add(img);
                }
                else
                {
                    resized.Add(img);
                }
            }

            for (int i = first; i <= last; i++)
            {
                Check(i, true);
            }
            for (int i = Math.Max(last + 1, first); i < count; i++)
            {
                Check(i, false);
            }
            for (int i = first - 1; i >= 0; i--)
            {
                Check(i, false);
            }
            return visible.Concat(outdated).Concat(resized).Take(max).ToList();
        }

        private void btnZoomOut_Click(object sender, EventArgs e)
        {
            StepThumbnailSize(-1);
        }

        private void btnZoomIn_Click(object sender, EventArgs e)
        {
            StepThumbnailSize(1);
        }

        #endregion

        #region Drag/Drop

        private async void thumbnailList1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            // Provide drag data
            if (SelectedIndices.Any())
            {
                var ido = await GetDataObjectForImages(SelectedImages, false);
                DoDragDrop(ido, DragDropEffects.Move | DragDropEffects.Copy);
            }
        }

        private void thumbnailList1_DragEnter(object sender, DragEventArgs e)
        {
            // Determine if drop data is compatible
            try
            {
                if (e.Data.GetDataPresent(typeof(DirectImageTransfer).FullName))
                {
                    var data = (DirectImageTransfer)e.Data.GetData(typeof(DirectImageTransfer).FullName);
                    e.Effect = data.ProcessID == Process.GetCurrentProcess().Id
                        ? DragDropEffects.Move
                        : DragDropEffects.Copy;
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorException("Error receiving drag/drop", ex);
            }
        }

        private async void thumbnailList1_DragDrop(object sender, DragEventArgs e)
        {
            // Receive drop data
            if (e.Data.GetDataPresent(typeof(DirectImageTransfer).FullName))
            {
                var data = (DirectImageTransfer)e.Data.GetData(typeof(DirectImageTransfer).FullName);
                if (data.ProcessID == Process.GetCurrentProcess().Id)
                {
                    DragMoveImages(e);
                }
                else
                {
                    await Task.Yield();
                    ImportDirect(data, false);
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (SelectedIndices.Any())
                {
                    var box = MessageBox.Show("�craser l'image s�lectionn�e", "Importation", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (box == DialogResult.Cancel)
                    {
                        return;
                    }

                }
                var data = (string[])e.Data.GetData(DataFormats.FileDrop);
                await Task.Yield();
                ImportFiles(data);
            }
            thumbnailList1.InsertionMark.Index = -1;
        }

        private void thumbnailList1_DragLeave(object sender, EventArgs e)
        {
            thumbnailList1.InsertionMark.Index = -1;
        }

        private void DragMoveImages(DragEventArgs e)
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            int index = GetDragIndex(e);
            if (index != -1)
            {
                var before = imageList.Images.ToList();
                UpdateThumbnails(imageList.MoveTo(SelectedIndices, index), true, true);
                thumbnailList1.GroupRefresh(imageList.Images);
                MarkChangedIfOrderChanged(before);
            }
        }

        private void MoveImages(IEnumerable<int> selection, int index)
        {
            if (index != -1)
            {
                var before = imageList.Images.ToList();
                UpdateThumbnails(imageList.MoveTo(selection, index), true, true);
                MarkChangedIfOrderChanged(before);
            }
        }

        // A click with a small mouse move starts a drag that drops the pages where they already are:
        // only a real change of the page order counts as a change (otherwise it asks to save for nothing).
        private void MarkChangedIfOrderChanged(List<ScannedImage> before)
        {
            if (!before.SequenceEqual(imageList.Images))
            {
                changeTracker.Made();
            }
        }


        private void thumbnailList1_DragOver(object sender, DragEventArgs e)
        {
            if (e.Effect == DragDropEffects.Move)
            {
               thumbnailList1.Refresh();
                var index = GetDragIndex(e);
                thumbnailList1.InsertionMark.Index = index;
                if (index == imageList.Images.Count)
                {
                    thumbnailList1.InsertionMark.Index = imageList.Images.Count-1;
                    thumbnailList1.InsertionMark.AppearsAfterItem = true;
                }
                else
                {
                    thumbnailList1.InsertionMark.AppearsAfterItem = false;
                }
                
            }
        }

        private int GetDragIndex(DragEventArgs e)
        {
            Point cp = thumbnailList1.PointToClient(new Point(e.X, e.Y));
            ListViewItem dragToItem = thumbnailList1.GetItemAt(cp.X, cp.Y);
            if (dragToItem == null)
            {
                var items = thumbnailList1.Items.Cast<ListViewItem>().ToList();
                var minY = items.Select(x => x.Bounds.Top).Min();
                var maxY = items.Select(x => x.Bounds.Bottom).Max();
                if (cp.Y < minY)
                {
                    cp.Y = minY;
                }
                if (cp.Y > maxY)
                {
                    cp.Y = maxY;
                }
                var row = items.Where(x => x.Bounds.Top <= cp.Y && x.Bounds.Bottom >= cp.Y).OrderBy(x => x.Bounds.X).ToList();
                dragToItem = row.FirstOrDefault(x => x.Bounds.Right >= cp.X) ?? row.LastOrDefault();
            }
            if (dragToItem == null)
            {
                return -1;
            }
            int dragToIndex = dragToItem.ImageIndex;
            if (cp.X > (dragToItem.Bounds.X + dragToItem.Bounds.Width / 2))
            {
                dragToIndex++;
            }
            return dragToIndex;
        }

        #endregion

        #region Documents

        // Nom du document à commencer à la première page de la prochaine numérisation : la feuille séparatrice
        // supprimée était la dernière page, il n'y avait pas encore de page pour commencer son document.
        // Utilisé seulement sur le fil de l'interface.
        private string pendingBarcodeDocumentName;

        // Extension ajoutée au nom tiré d'un code-barres : GroupRefresh lit le nom avec
        // Path.GetFileNameWithoutExtension, qui retirerait sinon tout ce qui suit le dernier point du code.
        private const string BarcodeDocumentNameSuffix = ".barcode";

        /// <summary>
        /// Nom de document tiré du texte lu (plusieurs codes : séparés par ^). Les caractères interdits dans un nom
        /// de fichier sont remplacés : le nom sert aussi aux exports, et Path.GetFileNameWithoutExtension
        /// (utilisé par GroupRefresh) plante sur ces caractères.
        /// </summary>
        private static string BarcodeDocumentName(string barcodeData)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var name = new string((barcodeData ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return name + BarcodeDocumentNameSuffix;
        }

        // Fait commencer un nouveau document à cette page (comme le menu « Ajouter un document »)
        private static void MarkDocumentStart(ScannedImage image, string separatorName)
        {
            image.Separator = true;
            image.RecoveryIndexImage.isSeparator = true;
            image.RecoveryIndexImage.SeparatorName = separatorName;
            image.Save();
        }

        // Pages de la feuille (recto et verso) dont fait partie la page index, dans la liste images
        private static void GetSheetRange(List<ScannedImage> images, int index, out int first, out int last)
        {
            first = last = index;
            // SheetSide : 1 = recto d'une feuille recto-verso, 2 = son verso, autre = recto seul
            if (images[index].SheetSide == 1 && index + 1 < images.Count && images[index + 1].SheetSide == 2)
            {
                last = index + 1;
            }
            else if (images[index].SheetSide == 2 && index > 0 && images[index - 1].SheetSide == 1)
            {
                first = index - 1;
            }
        }

        /// <summary>
        /// Option « Créer un nouveau document quand un code-barres est détecté » : appelée sur le fil de l'interface
        /// quand une détection se termine, avec les pages où un code a été lu.
        /// Chaque page avec un code commence un document nommé d'après le code ; les pages suivantes en font partie
        /// jusqu'au prochain code. Avec « Supprimer la feuille séparatrice », la feuille (recto et verso) est supprimée
        /// et c'est la page qui la suit qui commence le document.
        /// </summary>
        private void CreateDocumentsFromBarcodes(List<ScannedImage> pagesWithBarcode)
        {
            var config = userConfigManager.Config;
            if (closed || !config.BarcodeCreateDocument || pagesWithBarcode == null || pagesWithBarcode.Count == 0)
            {
                return;
            }

            List<ScannedImage> images;
            lock (imageList)
            {
                images = imageList.Images.ToList();
            }
            var withBarcode = new HashSet<ScannedImage>(pagesWithBarcode);
            // Dans l'ordre des pages (la détection en parallèle les trouve dans le désordre).
            // Une page supprimée entre-temps n'est plus dans la liste et est ignorée.
            var codeIndices = Enumerable.Range(0, images.Count).Where(i => withBarcode.Contains(images[i])).ToList();
            if (codeIndices.Count == 0)
            {
                return;
            }

            var toDelete = new SortedSet<int>();
            if (!config.BarcodeRemoveSeparatorSheet)
            {
                foreach (int i in codeIndices)
                {
                    MarkDocumentStart(images[i], BarcodeDocumentName(images[i].BarCodeData));
                }
            }
            else
            {
                // Page qui commence chaque document, et son nom. Deux feuilles séparatrices de suite :
                // la seconde l'emporte (le document de la première serait vide).
                var documentStarts = new SortedDictionary<int, string>();
                var sheets = new List<(int Last, string Name)>();
                foreach (int i in codeIndices)
                {
                    if (toDelete.Contains(i))
                    {
                        // Code aussi lu sur l'autre face d'une feuille déjà traitée
                        continue;
                    }
                    GetSheetRange(images, i, out int first, out int last);
                    for (int k = first; k <= last; k++)
                    {
                        toDelete.Add(k);
                    }
                    sheets.Add((last, BarcodeDocumentName(images[i].BarCodeData)));
                }
                foreach (var (last, name) in sheets)
                {
                    int start = last + 1;
                    while (start < images.Count && toDelete.Contains(start))
                    {
                        start++;
                    }
                    if (start < images.Count)
                    {
                        documentStarts[start] = name;
                    }
                    else
                    {
                        // La feuille est la dernière page : le document commencera avec la prochaine numérisation
                        pendingBarcodeDocumentName = name;
                    }
                }
                foreach (var start in documentStarts)
                {
                    MarkDocumentStart(images[start.Key], start.Value);
                }
            }

            if (toDelete.Count > 0)
            {
                // Comme la suppression manuelle (Delete) : fichiers, index et liste restent cohérents
                bool previewDeleted = SelectedIndices.Any(toDelete.Contains);
                if (previewDeleted)
                {
                    tiffViewerCtl1.Image = null;
                }
                imageList.Delete(toDelete.ToList());
                DeleteThumbnails();
            }
            thumbnailList1.GroupRefresh(imageList.Images);
            thumbnailList1.UpdateDescriptions(imageList.Images, Color.Black);
            changeTracker.Made();
            UpdateToolbar();
            DisplaySelectedItem_info();
        }

        /// <summary>
        /// Crée la détection des codes-barres ; à la fin, crée les documents si l'option est active.
        /// scannedPages : les pages d'une numérisation (détection automatique), pour y commencer le document en attente.
        /// Fait à la fin de la détection : à ce moment, toutes les pages de la numérisation sont dans la liste.
        /// </summary>
        private barCodeOperation CreateBarcodeOperation(List<ScannedImage> scannedPages = null)
        {
            var op = operationFactory.Create<barCodeOperation>();
            op.Finished += (sender, args) => SafeInvokeAsync(() =>
            {
                if (scannedPages != null)
                {
                    ApplyPendingBarcodeDocument(scannedPages);
                }
                CreateDocumentsFromBarcodes(op.ImagesWithBarcode);
                // Le texte du code-barres de la page sélectionnée doit se rafraîchir dès que la détection se termine
                DisplaySelectedItem_info();
            });
            return op;
        }

        // Le document en attente (feuille séparatrice supprimée en fin de numérisation) commence à la première page
        // de cette numérisation
        private void ApplyPendingBarcodeDocument(List<ScannedImage> scannedPages)
        {
            if (pendingBarcodeDocumentName == null || scannedPages.Count == 0)
            {
                return;
            }
            if (!userConfigManager.Config.BarcodeCreateDocument)
            {
                pendingBarcodeDocumentName = null;
                return;
            }
            var first = scannedPages[0];
            bool stillThere;
            lock (imageList)
            {
                stillThere = imageList.Images.Contains(first);
            }
            if (stillThere)
            {
                MarkDocumentStart(first, pendingBarcodeDocumentName);
                pendingBarcodeDocumentName = null;
                thumbnailList1.GroupRefresh(imageList.Images);
                thumbnailList1.UpdateDescriptions(imageList.Images, Color.Black);
            }
        }

        private void tsiDocumentAdd_Click(object sender, EventArgs e)
        {
            //Define a selected image as the start of a new document
            if (SelectedImages.Any())
            {
                var sel = Enumerable.Range(0, imageList.Images.Count);

                imageList.Images[SelectedIndices.First()].RecoveryIndexImage.isSeparator = true;
                imageList.Images[SelectedIndices.First()].Separator = true;
                imageList.Images[SelectedIndices.First()].Save();

                Color fore = Color.Black;
                if (darkMode)
                {
                    fore = Color.Black;
                }
                thumbnailList1.GroupRefresh(imageList.Images);
                thumbnailList1.UpdateDescriptions(imageList.Images, fore);
            }
        }

        private void tsiDocumentRemove_Click(object sender, EventArgs e)
        {
            if (SelectedImages.Any())
            {
                var sel = Enumerable.Range(0, imageList.Images.Count);

                imageList.Images[SelectedIndices.First()].RecoveryIndexImage.isSeparator = false;
                imageList.Images[SelectedIndices.First()].Separator = false;
                imageList.Images[SelectedIndices.First()].Save();


                Color fore = Color.Black;
                if (darkMode)
                {
                    fore = Color.Black;
                }
                thumbnailList1.GroupRefresh(imageList.Images);
                thumbnailList1.UpdateDescriptions(imageList.Images, fore);
            }

        }

        private void TSMI_expandAll_Click(object sender, EventArgs e)
        {
            thumbnailList1.SetGroupState(ListViewGroupState.Collapsible);
        }

        private void TSMI_contractall_Click(object sender, EventArgs e)
        {
            thumbnailList1.SetGroupState(ListViewGroupState.Collapsed | ListViewGroupState.Collapsible);
        }

        private void TSMI_RemoveAll_Click(object sender, EventArgs e)
        {
            documents.Clear();
            thumbnailList1.DestroyGroups(imageList.Images);
            thumbnailList1.GroupRefresh(imageList.Images);
            thumbnailList1.UpdateDescriptions(imageList.Images, Color.Black);
        }

        #endregion

        #region QuickView
        // Quickview use the panel 2, Thumbnails use panel 1
        private void tsShowHideView_Click(object sender, EventArgs e)
        {
            splitContainer1.Panel2Collapsed = !splitContainer1.Panel2Collapsed;

            //save the new status of the window panel
            UserConfigManager.Config.Quickview = splitContainer1.Panel2Collapsed;
            UserConfigManager.Save();
        }
        


        private void splitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
        {
            
            
            if (splitter1)
            {
                UserConfigManager.Config.Splitter1_distance = splitContainer1.SplitterDistance;
                UserConfigManager.Save();
                splitter1 = false;
            }
        }

        

        private void splitContainer1_SplitterMoving(object sender, SplitterCancelEventArgs e)
        {
            splitter1 = true;
        }
        #endregion

        #region new stuff
        private async void loadProjectTool_TSMI_Click_1(object sender, EventArgs e)
        {

            // True when the user wants the current pages kept (backed up) before the project is loaded
            bool saveCurrent = false;
            if (HasPagesToSave())
            {
                var result1 = MessageBox.Show(MiscResources.UnsavedChangesAction, MiscResources.UnsavedChanges,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                saveCurrent = result1 == DialogResult.Yes;
            }

            var openFileDialog = new OpenFileDialog();
            openFileDialog.CheckFileExists = false;
            openFileDialog.AutoUpgradeEnabled = true;
            openFileDialog.InitialDirectory = Paths.Projects;
            openFileDialog.ValidateNames = false;
            
            openFileDialog.Filter = "NAPS2 Index|index.xml|" + MiscResources.FileTypeAllFiles + "(*.*)|*.*";
            openFileDialog.FileName = " ";

            DialogResult result = openFileDialog.ShowDialog();

            // Only recover if the user acknoledge and don't change the project name. Also check if the folder contain a .lock file so it will not remove the content later.
            if (result == DialogResult.OK)
            {
                DirectoryInfo di = new DirectoryInfo(Path.GetDirectoryName(openFileDialog.FileName));
                if (RecoveryImage.IsAdopted && RecoveryImage._recoveryFolder != null &&
                    string.Equals(RecoveryImage._recoveryFolder.FullName.TrimEnd('\\'), di.FullName.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    // This project is already open
                    return;
                }
                if (File.Exists(Path.Combine(di.FullName, ".lock")))
                {
                    if (imageList.Images.Count > 0 && saveCurrent)
                    {
                        closeWorkspace(); // Backup the current project before getting a new one.
                    }
                    else
                    {
                        Clear(true);
                    }
                    userConfigManager.Config.project = projectName; //userConfigManager.Config.PdfSettings.DefaultFileName = projectName;
                    userConfigManager.Save();
                    recoveryManager.SetFolder(di); //Set to a folder other than the last used one.
                    //recover mode activated = (will not update the gui of the image preview while loading)
                    recover = true;
                    // Fast path: the project folder becomes the work folder and its index is read as is (no copy)
                    // The work runs in the background while a progress window shows the pages being loaded.
                    // Each page is sent to the list as soon as it is read (in batches), so the user sees what is loaded.
                    statusStrip1.Items[0].Text = "Loading project...";
                    var openOperation = recoveryManager.OpenProjectInPlace(ReceiveRecovery());
                    operationProgress.ShowModalProgress(openOperation);
                    bool adopted;
                    try
                    {
                        adopted = await openOperation.Success;
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancelled before the background task started
                        adopted = false;
                    }
                    if (!adopted && openOperation.WasCancelled)
                    {
                        // Cancelled (or window closed): the project is closed without being deleted or modified,
                        // then the application starts over like at startup, with a new work folder.
                        lock (pendingRecoveredImages)
                        {
                            pendingRecoveredImages.Clear();
                        }
                        // Release the project folder FIRST: without an index manager, removing the pages below
                        // can neither rewrite index.xml (DeferSave) nor delete a file of the project.
                        RecoveryImage.ReleaseAdopted();
                        SetRecover(false);
                        using (RecoveryImage.KeepFiles())
                        {
                            imageList.Delete(Enumerable.Range(0, imageList.Images.Count));
                        }
                        recoveryManager.SetFolder(null);
                        ImageSettingsContainer.ProjectSettings = new ProjectSettings();
                        imageSettingsContainer.Project_Settings = ImageSettingsContainer.ProjectSettings;
                        ResetWorkspaceToNew();
                        // Like at startup: a new work folder (new name) is created on the next scan or import
                        RecoveryImage.StartNewWorkFolder();
                        recover = false;
                        UserConfigManager.Config.project = projectName;
                        UserConfigManager.Save();
                        statusStrip1.Items[0].Text = MiscResources.No_selection;
                        UpdateToolbar();
                        return;
                    }
                    if (adopted)
                    {
                        // Sends any remaining buffered pages and ends the recovery mode
                        SetRecover(false);
                        // The project is fully loaded: select the first page and bring the list back to the top
                        if (imageList.Images.Count > 0)
                        {
                            SelectedIndices = Enumerable.Range(0, 1);
                            thumbnailList1.EnsureVisible(0);
                        }
                        // Replace the "Loading project..." text now that the list is complete
                        statusStrip1.Items[0].Text = MiscResources.No_selection;
                        // The pages come straight from the project files: loading them is not a change.
                        // (FlushRecoveredImages marks each batch as a change, which made the app ask to save.)
                        changeTracker.Clear();
                    }
                    else
                    {
                        statusStrip1.Items[0].Text = MiscResources.No_selection;
                        // Folder in use or unreadable index: fall back to loading a copy of the project
                        recoveryManager.RecoverScannedImages(ReceiveRecovery());
                    }
                    projectName = di.Name;

                    UserConfigManager.Config.project = projectName;
                    UserConfigManager.Save();
                    UpdateToolbar();
                    
                }         
            }         
        }
        public NotificationManager GetNotificationManager()
        {
            return this.notify;
        }

        public void RegenIconsList()
        {
            SafeInvokeAsync(() =>
            {

                thumbnailList1.RegenerateThumbnailList(imageList.Images, Color.Black, true);
                thumbnailList1.GroupRefresh(imageList.Images);
            });

              /*  thumbnailList1.Invoke(new MethodInvoker(delegate
                {
                    thumbnailList1.RegenerateThumbnailList(imageList.Images, Color.Black, true);
                    thumbnailList1.GroupRefresh(imageList.Images);
                }));*/
        }

        /// <summary>
        /// Looks for work folders left by a previous session that did not close properly and lets the user decide what to do.
        /// </summary>
        private void CheckUnfinishedWorkFolders()
        {
            var folders = RecoveryManager.FindUnfinishedWorkFolders();
            if (folders.Count == 0)
            {
                return;
            }

            var latest = folders[0];
            var result = MessageBox.Show(
                string.Format(MiscResources.UnfinishedWorkMessage, folders.Count, latest.Item2),
                MiscResources.UnfinishedWorkTitle,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);

            if (result == DialogResult.Yes)
            {
                // Copy the pages of the most recent folder into this session's work folder, then remove the old folder
                recoveryManager.SetFolder(latest.Item1);
                recover = true;
                recoveryManager.RecoverScannedImages(ReceiveRecovery(), true);
                UpdateToolbar();
            }
            else if (result == DialogResult.No)
            {
                foreach (var folder in folders)
                {
                    RecoveryManager.DeleteUnfinishedWorkFolder(folder.Item1);
                }
            }
        }

        /// <summary>
        /// Removes every page from the list. A project opened in place keeps its files on disk and is released.
        /// </summary>
        private void DeleteAllImagesKeepingProject()
        {
            if (RecoveryImage.IsAdopted)
            {
                recoveryManager.Save();
                using (RecoveryImage.KeepFiles())
                {
                    imageList.Delete(Enumerable.Range(0, imageList.Images.Count));
                }
                RecoveryImage.ReleaseAdopted();
            }
            else
            {
                imageList.Delete(Enumerable.Range(0, imageList.Images.Count));
            }
        }

        private void closeWorkspace(bool clear=true)
        {
            bool adopted = RecoveryImage.IsAdopted;
            //Recovery lock must be removed first to do operations (a project opened in place is already saved: no copy needed)
            if (adopted)
            {
                recoveryManager.Save();
            }
            else if (RecoveryImage._recoveryLock!=null)
                RecoveryImage._recoveryLock.Dispose();

            var todayDate = DateTime.Now;
            string strToday = todayDate.ToString("MM_dd_yyyy_HH_mm_ss"); // converts date to string as per current culture

            //Copy the work folder to another folder to keep it, if only it contain images
            if (!adopted && imageList.Images.Count() > 0)
            {
                // If the projectname was not named, will put the "untitled" and the date so it will be easier to find out later, else keep the defined name for the folder
                // Recursive: the "thumbs" cache folder is copied too, so the thumbnails are ready when the project is reopened
                if (projectName.Contains(string.Format(MiscResources.ProjectName)))
                    PathHelper.CopyDirectory(RecoveryImage.RecoveryFolder.FullName, Paths.Projects + "//" + projectName + "_" + strToday, true);
                else
                    PathHelper.CopyDirectory(RecoveryImage.RecoveryFolder.FullName, Paths.Projects + "//" + projectName, true);
            }  

            if (clear)
            {
                // Clear the images in the work folder and start as new
                tiffViewerCtl1.Image = null;

                if (imageList.Images.Count() > 0)
                {
                    DeleteAllImagesKeepingProject();
                }
                else if (adopted)
                {
                    RecoveryImage.ReleaseAdopted();
                }
                ResetWorkspaceToNew();
                notify.ParentForm = this;
            }
            else if (!clear)
            {
                if (changeTracker != null)
                    changeTracker.Clear();

                var result1 = MessageBox.Show("Changes have been saved!", MiscResources.UnsavedChanges ,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            }
            //Update the toolbar
            UpdateToolbar();
        }
        
        /// <summary>
        /// Resets the screen and the project state once every page was removed from the list:
        /// viewer, thumbnails, documents, unsaved changes, project name and selection. Nothing is deleted on disk here.
        /// Used when closing the workspace and when the loading of a project is cancelled.
        /// </summary>
        /// <summary>
        /// True when the current pages must be offered for saving: there are pages and either they were changed,
        /// or they only live in a temporary work folder (not a project opened in place).
        /// </summary>
        private bool HasPagesToSave()
        {
            return imageList.Images.Count > 0 && (changeTracker.HasUnsavedChanges || !RecoveryImage.IsAdopted);
        }

        private void ResetWorkspaceToNew()
        {
            ClearPreviewPrefetch();
            // Un document en attente appartenait au projet fermé
            pendingBarcodeDocumentName = null;
            bitmap = null;
            tiffViewerCtl1.Image = null;
            DeleteThumbnails();
            documents?.Clear();
            changeTracker?.Clear();
            projectName = string.Format(MiscResources.ProjectName);
            SelectedIndices = Enumerable.Range(0, 0);
        }

        // Closing the project and make fresh start (like new)
        private void closeCurrentProjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //Will backup the current projet if it contain images.
            closeWorkspace(true);                               
        }

        public static Color getCurrentModeFore()
        {
            if (darkMode)
            {
                return Color.White;
            }
            else
                return Color.Black;
        }

        public static Color getCurrentModeBack()
        {
            if (darkMode)
            {
                return Color.FromArgb(46, 46, 46); ;
            }
            else
                return Color.White;
        }
        // Ajust the interface to be themed in "dark mode" like windows.
        private bool SetDarkMode(bool input)
        {
            if (input)
            {
                BackgroundForm.UseImmersiveDarkMode(this.Handle, true);
                this.BackColor = Color.FromArgb(24, 24, 24);
                this.ForeColor = SystemColors.ControlLightLight;

               
                
                // Toolstrip
                toolStripContainer1.TopToolStripPanel.BackColor = Color.FromArgb(24, 24, 24);
                toolStripContainer1.BottomToolStripPanel.BackColor = Color.FromArgb(24, 24, 24);
                //tStrip.BackColor = Color.FromArgb(24, 24, 24);
                tStrip.BackColor = Color.FromArgb(46, 46, 46);
                TS_Index.BackColor = Color.FromArgb(46, 46, 46);
                TS_BarcodeInfo.BackColor = Color.FromArgb(24, 24, 24);
                TS_BarcodeInfo.ForeColor = Color.White;


                // Status strip
                statusStrip1.BackColor = SystemColors.ControlDarkDark;
                thumbnailList1.BackColor = Color.FromArgb(220, 220, 220);  //Color.FromArgb(36, 36, 36);
                tiffViewerCtl1.BackColor = Color.FromArgb(36, 36, 36);
                
                RegenIconsList();
            }
            else
            {
                BackgroundForm.UseImmersiveDarkMode(this.Handle, false);
                this.BackColor = Color.White;
                this.ForeColor = Color.FromArgb(24, 24, 24);

                thumbnailList1.BackColor = Color.White;
                tiffViewerCtl1.BackColor = Color.White;
                tiffViewerCtl1.tiffviewer1.BackColor = Color.White;

                //toolstrip
                toolStripContainer1.TopToolStripPanel.BackColor = Color.White;
                toolStripContainer1.BottomToolStripPanel.BackColor = Color.White;
                tStrip.BackColor = Color.White;
                BottomToolStripPanel.BackColor = Color.White;
                TS_Index.BackColor = Color.White;
                TS_BarcodeInfo.BackColor = Color.White;
                TS_BarcodeInfo.ForeColor = Color.Black;

                //status strip
                statusStrip1.BackColor = Color.White;
                // File menu
                FileDm.DropDown.BackColor = Color.White;
                FileDm.DropDown.ForeColor = Color.Black;
                // Scan menu
                tsScan.DropDown.BackColor = Color.White;
                tsScan.DropDown.ForeColor = Color.Black;
                // Profile menu
                tsCombo_Profiles.DropDown.BackColor = Color.White;
                tsCombo_Profiles.DropDown.ForeColor = Color.Black;
                // Image menu
                tsdImage.DropDown.BackColor = Color.White;
                tsdImage.DropDown.ForeColor = Color.Black;
                // Image menu
                tsdRotate.DropDown.BackColor = Color.White;
                tsdRotate.DropDown.ForeColor = Color.Black;
                // Reorder menu
                tsdReorder.DropDown.BackColor = Color.White;
                tsdReorder.DropDown.ForeColor = Color.Black;
                RegenIconsList();

            }
            return input;
        }

        private void TSI_ToggleDarkMode_Click(object sender, EventArgs e)
        {
            darkMode = SetDarkMode(!darkMode);
            UserConfigManager.Config.DarkMode = darkMode;
            UserConfigManager.Save();
        }

        private void BarCodeCheck()
        {
            var list = SelectedImages.ToList();
            //If there is no selection, it will select all images
            if (!SelectedIndices.Any())
            {
                list = imageList.Images.ToList();
            }

            // La détection lancée à la main crée aussi les documents si l'option est active
            var op = CreateBarcodeOperation();
            if (op.Start(list))
            {
                operationProgress.ShowProgress(op);
                changeTracker.Made();
                UpdateToolbar();
            }

        }

        private void tsBarCodeCheck_Click(object sender, EventArgs e)
        {
            BarCodeCheck();
            DisplaySelectedItem_info();

        }

        private void tsBarCodeFilter_Click(object sender, EventArgs e)
        {
            using (var form = FormFactory.Create<FBarcodeSettings>())
            {
                form.ShowDialog();
            }
        }

        private void ctxScanBarCode_Click(object sender, EventArgs e)
        {
            BarCodeCheck();
            DisplaySelectedItem_info();
        }

        private void ts_BarCode_define_Click(object sender, EventArgs e)
        {
            DisplaySelectedItem_info();
        }

        //New export panel
        private void tsExport_Click(object sender, EventArgs e)
        {
            SaveProjectImages(imageList.Images, true);
        }

        //Create a new project.
        private void tsPrjNew_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count() > 0 && !HasPagesToSave())
            {
                // Project opened in place and not modified: just close it, nothing to save
                Clear(true);
            }
            else if (imageList.Images.Count() > 0)
            {
                var result1 = MessageBox.Show(MiscResources.UnsavedChangesAction, MiscResources.UnsavedChanges,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (result1 == DialogResult.No)
                {
                    Clear(true);
                }
                if (result1 == DialogResult.Yes)
                {
                    closeWorkspace();
                }
            }
            // Close the current workspace (save if the previous was not)
           
            // Ask for the project name: 
            ChangeProjectName();
            
        }

        private void tsPrjConfig_Click(object sender, EventArgs e)
        {
            var form = FormFactory.Create<FConfigurePrj>();
            
            BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
            if (form.ShowDialog() == DialogResult.OK)
            {
                recoveryManager.Save();
                imageSettingsContainer.Project_Settings = ImageSettingsContainer.ProjectSettings.Clone();
            }
            UpdateToolbar();

        }

        public void ChangeProjectName()
        {
            var form = FormFactory.Create<FProjectName>();
            BackgroundForm.UseImmersiveDarkMode(form.Handle, darkMode);
            form.setFileName(projectName); // The "old" filename will be set
            form.ShowProjects(true);
            form.ShowDialog();
            if (form.DialogResult == DialogResult.OK)
            {
                projectName = form.getFileName();
                UpdateToolbar(); // Display the changes TODO: Have to change the way it's saved
            }
            //Set the default filename with the new project name - Need to be changed. 
            UserConfigManager.Config.project = projectName;
            UserConfigManager.Save();
        }

        private void TS_SavePrj_Click(object sender, EventArgs e)
        {
            ChangeProjectName();
        }
        #endregion

        

        private void BarCodeInfo_KeyPress(object sender, EventArgs e)
        {
           // There are keyboard event registered so update the data immediately. (Live editing)
            imageList.Images[thumbnailList1.SelectedItems[0].Index].BarCodeData = TS_BarcodeInfo.TextBox.Text;
            imageList.Images[thumbnailList1.SelectedItems[0].Index].RecoveryIndexImage.BarCode = TS_BarcodeInfo.TextBox.Text;
            imageList.Images[thumbnailList1.SelectedItems[0].Index].Save();
            DisplaySelectedItem_info(true);
         
        }

        private void SMI_BarCodeInfo_Click(object sender, EventArgs e)
        {
            TS_Index.Visible = !TS_Index.Visible;
            UserConfigManager.Config.IndexWindow = TS_Index.Visible;
            UserConfigManager.Save();

        }

        private void saveChangesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.closeWorkspace(false);
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

    }

    //Data object to keep the live document data
    public class Document
    {
        public int firstpage;
        public int lastpage;
        public string description;
        public string name;
    }


}