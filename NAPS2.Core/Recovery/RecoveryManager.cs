using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAPS2.ImportExport.Images;
using NAPS2.Lang.Resources;
using NAPS2.Logging;
using NAPS2.Operation;
using NAPS2.Scan.Images;
using NAPS2.Util;
using NAPS2.WinForms;

namespace NAPS2.Recovery
{
    public class RecoveryManager
    {
        public static DirectoryInfo folderToRecoverFrom;
        private readonly IFormFactory formFactory;
        private readonly ThumbnailRenderer thumbnailRenderer;
        private readonly IOperationProgress operationProgress;
        private readonly ImageSettingsContainer imageSettingsContainer;

        public RecoveryManager(IFormFactory formFactory, ThumbnailRenderer thumbnailRenderer, IOperationProgress operationProgress, ImageSettingsContainer imageSettingsContainer)
        {
            this.formFactory = formFactory;
            this.thumbnailRenderer = thumbnailRenderer;
            this.operationProgress = operationProgress;
            this.imageSettingsContainer = imageSettingsContainer;

        }
        public void SetFolder(DirectoryInfo info)
        {
            folderToRecoverFrom = info;
        }

        public const string WORK_FOLDER_PREFIX = "Work_folder_";

        /// <summary>
        /// Finds work folders left behind by NAPS2 instances that stopped without cleaning up (e.g. power loss).
        /// Folders still in use by a running instance are ignored. Empty folders are deleted.
        /// </summary>
        /// <returns>The folders with unsaved pages and their page count, most recently written first.</returns>
        public static List<Tuple<DirectoryInfo, int>> FindUnfinishedWorkFolders()
        {
            var result = new List<Tuple<DirectoryInfo, int>>();
            foreach (var folder in new DirectoryInfo(Paths.Projects).EnumerateDirectories(WORK_FOLDER_PREFIX + "*"))
            {
                // A work folder always has a lock file; without one it was not created by NAPS2's recovery system
                if (!File.Exists(Path.Combine(folder.FullName, RecoveryImage.LOCK_FILE_NAME)) || IsLocked(folder))
                {
                    continue;
                }
                try
                {
                    int imageCount = new RecoveryIndexManager(folder).Index.Images.Count;
                    if (imageCount == 0)
                    {
                        DeleteUnfinishedWorkFolder(folder);
                    }
                    else
                    {
                        result.Add(Tuple.Create(folder, imageCount));
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorException("Error reading unfinished work folder.", ex);
                }
            }
            return result.OrderByDescending(x => x.Item1.LastWriteTime).ToList();
        }

        /// <summary>
        /// Deletes an unfinished work folder, unless a running NAPS2 instance still holds its lock.
        /// </summary>
        public static void DeleteUnfinishedWorkFolder(DirectoryInfo folder)
        {
            try
            {
                if (folder.Exists && !IsLocked(folder))
                {
                    folder.Delete(true);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorException("Error deleting unfinished work folder.", ex);
            }
        }

        private static bool IsLocked(DirectoryInfo folder)
        {
            try
            {
                // RecoveryImage holds the lock file open with FileShare.None while the instance runs
                using (new FileStream(Path.Combine(folder.FullName, RecoveryImage.LOCK_FILE_NAME), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                return false;
            }
            catch (Exception)
            {
                // Treat anything unexpected as "in use" so that nothing is deleted by mistake
                return true;
            }
        }


        public void Save()
        {  // Save the project data from the data in the container.
            
            var recoveryIndexManager = new RecoveryIndexManager(RecoveryImage._recoveryFolder);
            recoveryIndexManager.Index.ProjectSettings = imageSettingsContainer.Project_Settings;
            recoveryIndexManager.Save();
        }

        public void RecoverScannedImages(Action<ScannedImage> imageCallback, bool deleteSourceAfterRecovery = false)
        {

            var op = new RecoveryOperation(formFactory, thumbnailRenderer);
            op.SetFolder(folderToRecoverFrom);
            op.DeleteSourceAfterRecovery = deleteSourceAfterRecovery;
            op.SetContainer(imageSettingsContainer);

            if (op.Start(imageCallback))
            {
                operationProgress.ShowProgress(op);
                                  
            }
        }

        public class RecoveryOperation : OperationBase
        {
            private readonly IFormFactory formFactory;
            private readonly ThumbnailRenderer thumbnailRenderer;
            private ImageSettingsContainer imageSettingsContainer;

            private FileStream lockFile;
            public static DirectoryInfo folderToRecoverFrom;
            private RecoveryIndexManager recoveryIndexManager;
            public int imageCount;
            private DateTime scannedDateTime;
            private bool cleanup;

            /// <summary>
            /// When true, the recovered folder is deleted after all of its pages were copied into the current work folder.
            /// </summary>
            public bool DeleteSourceAfterRecovery { get; set; }

            public void SetFolder(DirectoryInfo info)
            { 
                folderToRecoverFrom = info;
            }

            public RecoveryOperation(IFormFactory formFactory, ThumbnailRenderer thumbnailRenderer)
            {
                this.formFactory = formFactory;
                this.thumbnailRenderer = thumbnailRenderer;
                
                ProgressTitle = MiscResources.ImportProgress;
                
                AllowCancel = true;
                AllowBackground = true;
                cleanup = false;
                folderToRecoverFrom = null;
            }

            public void SetContainer(ImageSettingsContainer imageSettingsContainer)
            {
                this.imageSettingsContainer = imageSettingsContainer;
            }

            public bool Start(Action<ScannedImage> imageCallback)
            {
                Status = new OperationStatus();
                if (Status != null)
                {
                    Status.StatusText = MiscResources.Recovering;
                }
                
                try
                {
                    if (folderToRecoverFrom == null)
                    {
                        folderToRecoverFrom = FindAndLockFolderToRecoverFrom();
                        cleanup = true;
                    }
                    recoveryIndexManager = new RecoveryIndexManager(folderToRecoverFrom);
                    imageCount = recoveryIndexManager.Index.Images.Count;
                    scannedDateTime = folderToRecoverFrom.LastWriteTime;
                    if (cleanup)
                    {
                        if (imageCount == 0)
                        {
                            // If there are no images, do nothing and remove the folder for cleanup
                            ReleaseFolderLock();
                            DeleteFolder();
                            return false;
                        }
                        return false;
                    }
                    RunAsync(() =>
                    {
                        try
                        {
                            if (DoRecover(imageCallback))
                            {
                                    // Theses are not recovered but used as loading a previous projet, so no delete
                                    ReleaseFolderLock();
                                GC.Collect();
                                //DeleteFolder();
                                // Only remove the old folder once every page was copied (a cancel keeps it)
                                if (DeleteSourceAfterRecovery && !CancelToken.IsCancellationRequested)
                                {
                                    DeleteFolder();
                                }
                                return true;
                            }
                            return false;
                        }
                        finally
                        {
                            ReleaseFolderLock();
                            // Also runs after an exception: ends recovery mode and flushes any buffered pages
                            FDesktop.GetInstance().SetRecover(false);
                            GC.Collect();
                        }
                    });
                    return true;
                }
                catch (Exception)
                {
                    ReleaseFolderLock();
                    throw;
                }
               
            }
            //Older method. Async was removed since there is no async stuff in there
            //private async Task<bool> DoRecover(Action<ScannedImage> imageCallback)
            private bool DoRecover(Action<ScannedImage> imageCallback)
            {
                Status.MaxProgress = recoveryIndexManager.Index.Images.Count;
                //InvokeStatusChanged();

                //Tries to recover the project data
                ImageSettingsContainer.ProjectSettings = new ProjectSettings();
                ImageSettingsContainer.ProjectSettings.CSVFileName = recoveryIndexManager.Index.ProjectSettings.CSVFileName;
                ImageSettingsContainer.ProjectSettings.DefaultFileName = recoveryIndexManager.Index.ProjectSettings.DefaultFileName;
                ImageSettingsContainer.ProjectSettings.UseCSVExport = recoveryIndexManager.Index.ProjectSettings.UseCSVExport;
                ImageSettingsContainer.ProjectSettings.CSVExpression = recoveryIndexManager.Index.ProjectSettings.CSVExpression;
                ImageSettingsContainer.ProjectSettings.Name = recoveryIndexManager.Index.ProjectSettings.Name;

                imageSettingsContainer.Project_Settings = ImageSettingsContainer.ProjectSettings;

                FDesktop.GetInstance().SetRecover(true); //Set the application FLAG that it is in recovery
                foreach (RecoveryIndexImage indexImage in recoveryIndexManager.Index.Images)
                {
                    if (CancelToken.IsCancellationRequested)
                    {
                        FDesktop.GetInstance().SetRecover(false);
                        return true;
                    }

                    string imagePath = Path.Combine(folderToRecoverFrom.FullName, indexImage.FileName);
                    ScannedImage scannedImage;

                    if (".pdf".Equals(Path.GetExtension(imagePath), StringComparison.InvariantCultureIgnoreCase))
                    {
                        scannedImage = ScannedImage.FromSinglePagePdf(imagePath, true);
                    }
                    else
                    {
                        //Retrieve the information to store inside the image data of Naps.
                        using (var bitmap = new Bitmap(imagePath))
                        //using (var bitmap = new Bitmap(20,20))
                        {
                            scannedImage = ScannedImage.FromRecoveryFile(imagePath, indexImage.BitDepth, indexImage.HighQuality)
                                ?? new ScannedImage(bitmap, indexImage.BitDepth, indexImage.HighQuality, -1);                            
                            if (bitmap != null)
                            {
                                Size size = bitmap.Size;
                                scannedImage.infoResolution = size.Width + " px X " + size.Height + " px ";

                                //
                                string dpi = Math.Round(bitmap.HorizontalResolution).ToString();

                                string format = "Format: ";
                                format = bitmap.PixelFormat switch
                                {
                                    PixelFormat.Format24bppRgb => format + "Color 24bit, DPI: " + dpi,
                                    PixelFormat.Format32bppArgb => format + "Color 32bit, DPI: " + dpi,
                                    PixelFormat.Format8bppIndexed => format + "Indexed Color 8bit, DPI: " + dpi,
                                    PixelFormat.Format1bppIndexed => format + "Bitonal, DPI: " + dpi,
                                    _ => "DPI: " + dpi,
                                };
                                scannedImage.infoFormat = format;

                                //Safety to put a name in case the separator name is empty
                                if (scannedImage.RecoveryIndexImage.SeparatorName == "")
                                    scannedImage.RecoveryIndexImage.SeparatorName = imagePath;
                            }
                        }
                    }

                    //Put back the informations
                    scannedImage.RecoveryIndexImage.BarCode = scannedImage.BarCodeData = indexImage.BarCode;
                    scannedImage.RecoveryIndexImage.SheetSide = scannedImage.SheetSide = indexImage.SheetSide;
                    scannedImage.RecoveryIndexImage.isSeparator = scannedImage.Separator = indexImage.isSeparator;
                    scannedImage.RecoveryIndexImage.SeparatorName = indexImage.SeparatorName;

                    foreach (var transform in indexImage.TransformList)
                    {
                        scannedImage.AddTransform(transform);
                    }

                    //Temporary commented off to see if it increase performance. Update: This really increase performance.
                    //scannedImage.SetThumbnail(await thumbnailRenderer.RenderThumbnail(scannedImage));
                    
                    imageCallback(scannedImage);
                    Status.StatusText = string.Format(MiscResources.ActiveOperations, Path.GetFileName(indexImage.FileName));
                    Status.CurrentProgress++;
                    this.OnProgress(Status.CurrentProgress, Status.MaxProgress);
                    InvokeStatusChanged();
                   
                    
                }

                // Tell the Desktop class that recovery is completed and ask to redraw the numbers of the items
                FDesktop.GetInstance().SetRecover(false);
                FDesktop.GetInstance().RegenIconsList();
                return true;
            }

            public void DeleteFolder()
            {
                try
                {
                    ReleaseFolderLock();
                    //Only delete the folder if the temporary .lock file is present. Don't delete any other folder.
                    if (File.Exists(Path.Combine(folderToRecoverFrom.FullName, ".lock")))
                        folderToRecoverFrom.Delete(true);
                }
                catch (Exception ex)
                {
                    Log.ErrorException("Error deleting recovery folder.", ex);
                }
            }

            private DirectoryInfo FindAndLockFolderToRecoverFrom()
            {
                // Find the most recent recovery folder that can be locked (i.e. isn't in use already)
                return new DirectoryInfo(Paths.Projects)
                    .EnumerateDirectories()
                    .OrderByDescending(x => x.LastWriteTime)
                    .FirstOrDefault(TryLockRecoveryFolder);
            }

            public void ReleaseFolderLock()
            {
                // Unlock the recover folder
                if (lockFile != null)
                {
                    lockFile.Dispose();
                    lockFile = null;
                }
            }

            private bool TryLockRecoveryFolder(DirectoryInfo recoveryFolder)
            {
                try
                {
                    string lockFilePath = Path.Combine(recoveryFolder.FullName, RecoveryImage.LOCK_FILE_NAME);
                    lockFile = new FileStream(lockFilePath, FileMode.Open);
                    return true;
                }
                catch (Exception)
                {
                    // Some problem, e.g. the folder is already locked
                    return false;
                }
            }
        }
    }
}
