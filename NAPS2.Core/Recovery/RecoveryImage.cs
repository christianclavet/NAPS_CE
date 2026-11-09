using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.Logging;
using NAPS2.Scan;
using NAPS2.Scan.Images;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Util;

namespace NAPS2.Recovery
{
    public class RecoveryImage : IDisposable
    {
        public const string LOCK_FILE_NAME = ".lock";
        public static string batch_name = "Work_folder_"+Path.GetRandomFileName();
        private static readonly string RecoveryFolderPath = Path.Combine(Paths.Projects, batch_name );

        public static DirectoryInfo _recoveryFolder;
        public static FileInfo _recoveryLockFile;
        public static FileStream _recoveryLock;
        private static RecoveryIndexManager _recoveryIndexManager;

        private static int _recoveryFileNumber = 1;
        private static readonly object RecoveryFileNumberLock = new object();

        private static readonly DeferredAction DeferredSave = new DeferredAction(() =>
        {
            if (_recoveryIndexManager != null)
            {
                lock (_recoveryIndexManager)
                {
                    if (_recoveryFolder != null)
                    {
                        _recoveryIndexManager.Save();
                    }
                }
            }
        });

        public static bool DisableRecoveryCleanup { get; set; }

        public static DirectoryInfo RecoveryFolder
        {
            get
            {
                if (_recoveryFolder == null)
                {
                    // Automatically create a recovery folder owned by this process
                    _recoveryFolder = new DirectoryInfo(RecoveryFolderPath);
                    _recoveryFolder.Create();
                    if ((_recoveryFolder.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
                    {
                        //Add Hidden flag    
                        _recoveryFolder.Attributes |= FileAttributes.Hidden;
                    }
                    _recoveryLockFile = new FileInfo(Path.Combine(_recoveryFolder.FullName, LOCK_FILE_NAME));
                    _recoveryLock = _recoveryLockFile.Open(FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    _recoveryIndexManager = new RecoveryIndexManager(_recoveryFolder);
                }
                return _recoveryFolder;
            }
            set
            {
                if (_recoveryIndexManager != null)
                {
                    // Oops, this process already owns a recovery folder
                    throw new InvalidOperationException();
                }
                // If _recoveryFolder is set like this then this process doesn't own it, so we can't modify the index or lock
                _recoveryFolder = value;
            }
        }

        public static IDisposable DeferSave()
        {
            return DeferredSave.Defer();
        }

        /// <summary>
        /// Deletes the work folder owned by this process, discarding any pages that were not saved as a project.
        /// Folders owned by other processes or saved projects are never touched.
        /// </summary>
        public static void DeleteWorkFolder()
        {
            // Only delete a folder this process owns (created and locked by this process)
            if (IsAdopted)
            {
                // A saved project opened in place is never deleted, only released
                ReleaseAdopted();
                return;
            }
            if (DisableRecoveryCleanup || _recoveryIndexManager == null)
            {
                return;
            }
            try
            {
                // The lock must be released before the folder can be deleted
                if (_recoveryLock != null)
                {
                    _recoveryLock.Dispose();
                    _recoveryLock = null;
                }
                if (_recoveryFolder != null && _recoveryFolder.Exists)
                {
                    _recoveryFolder.Delete(true);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorException("Error deleting work folder.", ex);
            }
            finally
            {
                _recoveryFolder = null;
            }
        }


        public static RecoveryImage CreateNew(ImageFormat fileFormat, ScanBitDepth bitDepth, bool highQuality, List<Transform> transformList)
        {
            return new RecoveryImage(fileFormat, bitDepth, highQuality, transformList);
        }

        public static RecoveryImage LoadExisting(RecoveryIndexImage recoveryIndexImage)
        {
            return new RecoveryImage(recoveryIndexImage, false);
        }

        /// <summary>
        /// Wraps an entry of an adopted project's index. The entry is already in the index and its file already exists.
        /// </summary>
        public static RecoveryImage LoadAdopted(RecoveryIndexImage recoveryIndexImage)
        {
            return new RecoveryImage(recoveryIndexImage, true);
        }

        /// <summary>
        /// True when the current folder is a saved project opened in place (not a temporary work folder).
        /// Its files are never deleted when the project is closed.
        /// </summary>
        public static bool IsAdopted { get; private set; }

        /// <summary>
        /// Incremented each time the recovery folder changes, so workers holding the old path can be restarted.
        /// </summary>
        public static int FolderVersion { get; private set; }

        public static RecoveryIndexManager CurrentIndexManager => _recoveryIndexManager;

        private static int keepFilesCount;

        /// <summary>
        /// While the returned scope is open, disposing pages does not delete their files or index entries.
        /// </summary>
        public static IDisposable KeepFiles()
        {
            Interlocked.Increment(ref keepFilesCount);
            return new KeepFilesScope();
        }

        private class KeepFilesScope : IDisposable
        {
            public void Dispose()
            {
                Interlocked.Decrement(ref keepFilesCount);
            }
        }

        /// <summary>
        /// Makes a saved project folder the current recovery folder, so it can be opened without copying anything.
        /// Returns false (nothing changed) when the folder is in use or has no usable index.
        /// </summary>
        public static bool TryAdopt(DirectoryInfo folder, out RecoveryIndex index)
        {
            index = null;
            FileStream newLock = null;
            try
            {
                newLock = new FileStream(Path.Combine(folder.FullName, LOCK_FILE_NAME), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var manager = new RecoveryIndexManager(folder);
                index = manager.Index;
                // Ignore entries whose file is gone
                index.Images.RemoveAll(x => !File.Exists(Path.Combine(folder.FullName, x.FileName)));

                // Release whatever this process owned before
                if (IsAdopted)
                {
                    ReleaseAdopted();
                }
                else
                {
                    DeleteWorkFolder();
                }
                _recoveryFolder = folder;
                _recoveryLockFile = new FileInfo(Path.Combine(folder.FullName, LOCK_FILE_NAME));
                _recoveryLock = newLock;
                _recoveryIndexManager = manager;
                IsAdopted = true;
                FolderVersion++;
                return true;
            }
            catch (Exception ex)
            {
                newLock?.Dispose();
                Log.ErrorException("Could not open the project in place.", ex);
                index = null;
                return false;
            }
        }

        /// <summary>
        /// Stops using an adopted project folder without touching its files. A new work folder is created on next use.
        /// </summary>
        public static void ReleaseAdopted()
        {
            if (!IsAdopted)
            {
                return;
            }
            _recoveryLock?.Dispose();
            _recoveryLock = null;
            _recoveryLockFile = null;
            _recoveryIndexManager = null;
            _recoveryFolder = null;
            IsAdopted = false;
            FolderVersion++;
        }

        public static void Refresh(IEnumerable<ScannedImage> images)
        {
            if (_recoveryIndexManager != null)
            {
                // Wait for each page's background encode, so only pages that have a file are indexed.
                // This happens before taking the lock: the encode itself needs the lock to save the index.
                var indexImages = images.Select(x =>
                {
                    x.WaitForRecoveryFile();
                    return x.RecoveryIndexImage;
                }).ToList();
                lock (_recoveryIndexManager)
                {
                    _recoveryIndexManager.Index.Images.Clear();
                    _recoveryIndexManager.Index.Images.AddRange(indexImages);
                    _recoveryIndexManager.Save();
                }
            }
        }

        public static string GetNextFileName()
        {
            lock (RecoveryFileNumberLock)
            {
                // Use an incrementing number + the process ID to ensure uniqueness
                return $"{Process.GetCurrentProcess().Id}_{(_recoveryFileNumber++).ToString("D5", CultureInfo.InvariantCulture)}";
            }
        }

        private static string GetExtension(ImageFormat imageFormat)
        {
            if (ReferenceEquals(imageFormat, null))
            {
                return ".pdf";
            }
            if (Equals(imageFormat, ImageFormat.Png))
            {
                return ".png";
            }
            if (Equals(imageFormat, ImageFormat.Jpeg))
            {
                return ".jpg";
            }
            throw new ArgumentException();
        }

        private bool saved;
        private bool disposed;

        private RecoveryImage(ImageFormat fileFormat, ScanBitDepth bitDepth, bool highQuality, List<Transform> transformList)
        {
            FileFormat = fileFormat;
            FileName = GetNextFileName() + GetExtension(FileFormat);
             
            FilePath = Path.Combine(RecoveryFolder.FullName, FileName);
            IndexImage = new RecoveryIndexImage
            {
                FileName = FileName,
                BitDepth = bitDepth,
                BarCode = barCode,
                HighQuality = highQuality,
                TransformList = transformList,
                SheetSide = SheetSide,
                isSeparator = IsSeparator,
                SeparatorName = SeparatorName,
            };
        }

        private RecoveryImage(RecoveryIndexImage recoveryIndexImage, bool adopted)
        {
            if (adopted)
            {
                FileFormat = ".png".Equals(Path.GetExtension(recoveryIndexImage.FileName), StringComparison.InvariantCultureIgnoreCase) ? ImageFormat.Png
                    : ".pdf".Equals(Path.GetExtension(recoveryIndexImage.FileName), StringComparison.InvariantCultureIgnoreCase) ? null
                    : ImageFormat.Jpeg;
                FileName = recoveryIndexImage.FileName;
                barCode = recoveryIndexImage.BarCode;
                SheetSide = recoveryIndexImage.SheetSide;
                IsSeparator = recoveryIndexImage.isSeparator;
                SeparatorName = recoveryIndexImage.SeparatorName;
                FilePath = Path.Combine(RecoveryFolder.FullName, FileName);
                IndexImage = recoveryIndexImage;
                saved = true;
                return;
            }
            if (_recoveryIndexManager != null && _recoveryIndexManager.Index.Images.Contains(recoveryIndexImage))
            {
                throw new ArgumentException("Recovery image already exists in index");
            }

            string ext = Path.GetExtension(recoveryIndexImage.FileName);
            FileFormat = ".png".Equals(ext, StringComparison.InvariantCultureIgnoreCase) ? ImageFormat.Png
                : ".pdf".Equals(ext, StringComparison.InvariantCultureIgnoreCase) ? null
                : ImageFormat.Jpeg;
            FileName = recoveryIndexImage.FileName;
            barCode = recoveryIndexImage.BarCode;
            SheetSide = recoveryIndexImage.SheetSide;
            IsSeparator = recoveryIndexImage.isSeparator;
            SeparatorName = recoveryIndexImage.SeparatorName;

            FilePath = Path.Combine(RecoveryFolder.FullName, FileName);
            IndexImage = recoveryIndexImage;
            Save();
        }

        public ImageFormat FileFormat { get; }

        public string FileName { get; }

        public string FilePath { get; }

        public string barCode { get; set; }

        public int SheetSide { get; set; }
        public bool IsSeparator { get; set; }
        public string SeparatorName { get; set; }

        public RecoveryIndexImage IndexImage { get; }

        public void Save()
        {
            if (disposed)
            {
                throw new InvalidOperationException();
            }
            if (_recoveryIndexManager != null)
            {
                lock (_recoveryIndexManager)
                {
                    if (!saved)
                    {
                        _recoveryIndexManager.Index.Images.Add(IndexImage);
                        saved = true;
                    }
                    // While a batch operation defers saving (e.g. loading a project), the index is written once at the end
                    if (!DeferredSave.IsDeferred)
                    {
                        _recoveryIndexManager.Save();
                    }
                }
            }
        }

        public void Move(int index)
        {
            if (!saved || disposed || _recoveryIndexManager == null)
            {
                throw new InvalidOperationException();
            }
            lock (_recoveryIndexManager)
            {
                _recoveryIndexManager.Index.Images.Remove(IndexImage);
                _recoveryIndexManager.Index.Images.Insert(index, IndexImage);
                _recoveryIndexManager.Save();
            }
        }

        public void Dispose()
        {
            try
            {
                if (_recoveryIndexManager != null && !DisableRecoveryCleanup && Volatile.Read(ref keepFilesCount) == 0 && File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                    lock (_recoveryIndexManager)
                    {
                        _recoveryIndexManager.Index.Images.Remove(IndexImage);
                        if (!DeferredSave.IsDeferred)
                        {
                            _recoveryIndexManager.Save();
                        }
                    }
                    if (_recoveryIndexManager.Index.Images.Count == 0 && !IsAdopted)
                    {
                        _recoveryLock.Dispose();
                        RecoveryFolder.Delete(true);
                        _recoveryFolder = null;
                    }
                }
            }
            catch (IOException ex)
            {
                Log.ErrorException("Error cleaning up recovery files.", ex);
            }
            disposed = true;
        }
    }
}
