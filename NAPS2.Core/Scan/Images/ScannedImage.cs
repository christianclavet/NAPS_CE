using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.ImportExport;
using NAPS2.Logging;
using NAPS2.Recovery;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Util;

namespace NAPS2.Scan.Images
{
    public class ScannedImage : IDisposable
    {
        // Store the base image and metadata on disk using a separate class to manage lifetime
        // If NAPS2 crashes, the image data can be recovered by the next instance of NAPS2 to start
        private RecoveryImage recoveryImage;

        // Store a base image and transform pair (rather than doing the actual transform on the base image)
        // so that JPEG degradation is minimized when multiple rotations/flips are performed
        private List<Transform> transformList;

        // patch CC
        private readonly bool highQuality;

        private Bitmap thumbnail;
        private int thumbnailState;
        private int transformState;
        public string infoResolution;
        public string infoFormat;
        public string barCodeData;

        private bool disposed;
        private int snapshotCount;

        // Background encoding state for images created from a bitmap.
        // Until the encode finishes, the image has no file on disk and its index entry is not saved yet.
        // recoveryReady is only changed while holding lock(this).
        private Task encodeTask;
        private bool recoveryReady = true;

        // Limits how many bitmap copies can wait for encoding at once, which bounds memory use.
        private static readonly SemaphoreSlim pendingEncodes = new SemaphoreSlim(3, 3);

        public static ScannedImage FromSinglePagePdf(string pdfPath, bool copy)
        {
            return new ScannedImage(pdfPath, copy);
        }

        public void Update(string barCodeData)
        {
            WaitForRecoveryFile();
            recoveryImage.barCode = barCodeData;
            recoveryImage.Save();
        }

        public void Save()
        {
            WaitForRecoveryFile();
            recoveryImage.Save();
        }

        public ScannedImage(Bitmap img, ScanBitDepth bitDepth, bool highQuality, int quality)
        {
            this.highQuality = highQuality;
            transformList = new List<Transform>();

            // The format comes from the scan profile and is decided here, without encoding.
            var format = ScannedImageHelper.GetRecoveryFormat(img, bitDepth, highQuality);
            recoveryImage = RecoveryImage.CreateNew(format, bitDepth, highQuality, transformList);
            recoveryReady = false;

            // The caller usually disposes its bitmap right after this constructor, so encode a copy.
            // Waiting here applies back-pressure when several encodes are already pending.
            pendingEncodes.Wait();
            Bitmap copy;
            try
            {
                copy = (Bitmap)img.Clone();
            }
            catch
            {
                pendingEncodes.Release();
                throw;
            }
            encodeTask = Task.Run(() => EncodeInBackground(copy, bitDepth, quality));
           
        }

        // Runs on a background thread. Encodes the copy into the recovery file, then adds the index entry.
        private void EncodeInBackground(Bitmap copy, ScanBitDepth bitDepth, int quality)
        {
            try
            {
                ScannedImageHelper.EncodeForRecovery(copy, recoveryImage.FileFormat, bitDepth, quality, recoveryImage.FilePath);
                lock (this)
                {
                    // Saves the index with the current transforms, including any added while encoding
                    recoveryReady = true;
                    recoveryImage.Save();
                }
            }
            catch (Exception e)
            {
                // The image stays out of the recovery index. Previously this failure was not reported at all.
                Log.ErrorException("Error encoding a scanned image for recovery", e);
            }
            finally
            {
                copy.Dispose();
                pendingEncodes.Release();
            }
        }

        /// <summary>
        /// Blocks until the background encode has finished, so the recovery file and index entry exist.
        /// Call this before anything that needs the file on disk. It returns immediately for images that
        /// were created from a file or from the recovery index.
        /// </summary>
        public void WaitForRecoveryFile()
        {
            encodeTask?.Wait();
        }

        //Store the informations inside the image to save to the recovery
        public ScannedImage(RecoveryIndexImage recoveryIndexImage)
        {
            recoveryImage = RecoveryImage.LoadExisting(recoveryIndexImage);
            transformList = recoveryImage.IndexImage.TransformList;
            barCodeData = recoveryImage.barCode = recoveryIndexImage.BarCode;
            SheetSide = recoveryImage.SheetSide = recoveryIndexImage.SheetSide;
            Separator = recoveryImage.IsSeparator = recoveryIndexImage.isSeparator;
            recoveryImage.Save();

        }

        private ScannedImage(string pdfPath, bool copy)
        {
            transformList = new List<Transform>();
            recoveryImage = RecoveryImage.CreateNew(null, ScanBitDepth.C24Bit, false, transformList);
            recoveryImage.barCode = barCodeData;
            recoveryImage.IsSeparator = Separator;
            recoveryImage.SheetSide = SheetSide;

            if (copy)
            {
                File.Copy(pdfPath, recoveryImage.FilePath);
            }
            else
            {
                File.Move(pdfPath, recoveryImage.FilePath);
            }

            recoveryImage.Save();
        }

        // Stores an already encoded image file in the recovery folder by copying it (used by the import fast path).
        public static ScannedImage FromImportedFile(string imagePath, ImageFormat format, ScanBitDepth bitDepth, bool highQuality)
        {
            return new ScannedImage(imagePath, format, bitDepth, highQuality);
        }

        // Reuses an already encoded recovery file by copying it, instead of decoding and re-encoding the page.
        // Returns null when the file is not a PNG or JPEG, so the caller can fall back to the bitmap path.
        public static ScannedImage FromRecoveryFile(string imagePath, ScanBitDepth bitDepth, bool highQuality)
        {
            string ext = Path.GetExtension(imagePath);
            ImageFormat format;
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                format = ImageFormat.Png;
            }
            else if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                format = ImageFormat.Jpeg;
            }
            else
            {
                return null;
            }
            return new ScannedImage(imagePath, format, bitDepth, highQuality);
        }

        private ScannedImage(string imagePath, ImageFormat format, ScanBitDepth bitDepth, bool highQuality)
        {
            this.highQuality = highQuality;
            transformList = new List<Transform>();
            recoveryImage = RecoveryImage.CreateNew(format, bitDepth, highQuality, transformList);
            File.Copy(imagePath, recoveryImage.FilePath);
            recoveryImage.Save();
        }


        public int SheetSide { get; set; }

        public PatchCode PatchCode { get; set; }

        public string BarCodeData { get; set; }
        //public string BarCodeData => recoveryImage.barCode;

        public bool Separator { get; set; }

        public ImageFormat FileFormat => recoveryImage.FileFormat;

        public RecoveryIndexImage RecoveryIndexImage => recoveryImage.IndexImage;

        public string RecoveryFilePath
        {
            get
            {
                WaitForRecoveryFile();
                return recoveryImage.FilePath;
            }
        }

        public long Size
        {
            get
            {
                WaitForRecoveryFile();
                return new FileInfo(recoveryImage.FilePath).Length;
            }
        }

        public void Dispose()
        {
            // Wait first, so the encode does not write a file after it has been deleted
            WaitForRecoveryFile();
            lock (this)
            {
                disposed = true;
                // TODO: Does this work as intended? Since the recovery image isn't removed from the index
                if (snapshotCount != 0) return;

                // Delete the image data on disk
                recoveryImage?.Dispose();
                if (thumbnail != null)
                {
                    thumbnail.Dispose();
                    thumbnail = null;
                }

                FullyDisposed?.Invoke(this, new EventArgs());
            }
        }

        public void AddTransform(Transform transform)
        {
            bool save;
            lock (this)
            {
                // Also updates the recovery index since they reference the same list
                if (!Transform.AddOrSimplify(transformList, transform))
                {
                    return;
                }
                transformState++;
                // While the file is still encoding, the encode step saves the index and includes this change
                save = recoveryReady;
            }
            if (save)
            {
                recoveryImage.Save();
            }
            ThumbnailInvalidated?.Invoke(this, new EventArgs());
        }

        public void ResetTransforms()
        {
            bool save;
            lock (this)
            {
                if (transformList.Count == 0)
                {
                    return;
                }
                transformList.Clear();
                transformState++;
                save = recoveryReady;
            }
            if (save)
            {
                recoveryImage.Save();
            }
            ThumbnailInvalidated?.Invoke(this, new EventArgs());
        }

        public Bitmap GetThumbnail()
        {
            lock (this)
            {
                return (Bitmap) thumbnail?.Clone();
            }
        }

        public void SetThumbnail(Bitmap bitmap, int? state = null)
        {
            lock (this)
            {
                thumbnail?.Dispose();
                thumbnail = bitmap;
                thumbnailState = state ?? transformState;
                
            }
            ThumbnailChanged?.Invoke(this, new EventArgs());
        }

        public bool IsThumbnailDirty => thumbnailState != transformState;

        public EventHandler ThumbnailChanged;

        public EventHandler ThumbnailInvalidated;

        public EventHandler FullyDisposed;

        public void MovedTo(int index)
        {
            // Move requires the index entry to exist, so wait for the encode
            WaitForRecoveryFile();
            recoveryImage.Move(index);
        }

        public bool IsHighQuality()
        {
            return highQuality;
        }

        public Snapshot Preserve() => new Snapshot(this);

        [Serializable]
        [KnownType("KnownTypes")]
        public class Snapshot : IDisposable, ISerializable
        {
            private bool disposed;

            internal Snapshot(ScannedImage source)
            {
                lock (source)
                {
                    if (source.disposed)
                    {
                        throw new ObjectDisposedException("source");
                    }
                    source.snapshotCount++;
                    Source = source;
                    TransformList = source.transformList.ToList();
                    TransformState = source.transformState;
                    BarCodeData = source.barCodeData;
                    SheetSide = source.SheetSide;
                    Separator = source.Separator;
                    
                }
            }

            public ScannedImage Source { get; }

            public string BarCodeData { get; set; }

            public int SheetSide { get; set; }

            public bool Separator { get; set; }

            public List<Transform> TransformList { get; }

            public int TransformState { get; }

            public void Dispose()
            {
                if (disposed) return;
                lock (Source)
                {
                    disposed = true;
                    Source.snapshotCount--;
                    if (Source.disposed && Source.snapshotCount == 0)
                    {
                        Source.Dispose();
                    }
                }
            }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                Source.WaitForRecoveryFile();
                info.AddValue("RecoveryIndexImage", Source.RecoveryIndexImage);
                info.AddValue("TransformList", TransformList);
                info.AddValue("TransformState", TransformState);
                info.AddValue("BarcodeInfo", BarCodeData);
            }

            private Snapshot(SerializationInfo info, StreamingContext context)
            {
                Source = new ScannedImage((RecoveryIndexImage)info.GetValue("RecoveryIndexImage", typeof(RecoveryIndexImage)));
                TransformList = (List<Transform>)info.GetValue("TransformList", typeof(List<Transform>));
                TransformState = (int)info.GetValue("TransformState", typeof(int));
                BarCodeData = info.GetString("BarcodeInfo");
                
            }

            // ReSharper disable once UnusedMember.Local
            private static Type[] KnownTypes()
            {
                var transformTypes = Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsSubclassOf(typeof(Transform)));
                return transformTypes.Concat(new[] { typeof(List<Transform>), typeof(RecoveryIndexImage) }).ToArray();
            }
        }
    }
}
