using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using NAPS2.Lang.Resources;
using NAPS2.Operation;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Scan;
using NAPS2.Util;
using NAPS2.Recovery;
using NAPS2.Config;

namespace NAPS2.Scan.Images
{
    public class barCodeOperation : OperationBase
    {
        private readonly ScannedImageRenderer scannedImageRenderer;
        private readonly IUserConfigManager userConfigManager;

        public barCodeOperation(ScannedImageRenderer scannedImageRenderer, IUserConfigManager userConfigManager)
        {
            this.scannedImageRenderer = scannedImageRenderer;
            this.userConfigManager = userConfigManager;
    
            AllowCancel = true;
            AllowBackground = true;
        }

        // Pages de cette détection où au moins un code a été lu (après le filtre de format)
        private readonly List<ScannedImage> imagesWithBarcode = new List<ScannedImage>();

        /// <summary>
        /// Pages où cette détection a lu au moins un code. Complet une fois l'opération terminée
        /// (y compris annulée : seulement les pages traitées avant l'annulation).
        /// </summary>
        public List<ScannedImage> ImagesWithBarcode
        {
            get
            {
                lock (imagesWithBarcode)
                {
                    return imagesWithBarcode.ToList();
                }
            }
        }

        public bool Start(ICollection<ScannedImage> images)
        {
            // Applique le filtre de format choisi dans les réglages, avant le traitement parallèle
            var config = userConfigManager.Config;
            PatchCodeDetector.RequiredFormatPattern = config.BarcodeFilterEnabled && !string.IsNullOrWhiteSpace(config.BarcodeFilterPattern)
                ? config.BarcodeFilterPattern
                : null;
            PatchCodeDetector.DetectMultiple = config.BarcodeDetectMultiple;

            ProgressTitle = MiscResources.BarcodeTitle;
            Status = new OperationStatus
            {
                StatusText = MiscResources.BarcodeProgress,
                MaxProgress = images.Count,
     
            };
            
            RunAsync(() =>
            {
                var memoryLimitingSem = new Semaphore(4, 4);
                Pipeline.For(images).StepParallel(img =>
                {
                    if (CancelToken.IsCancellationRequested)
                    {
                        return null;
                    }
                    memoryLimitingSem.WaitOne();
                    Bitmap bitmap;
                    try
                    {
                        bitmap = scannedImageRenderer.Render(img).Result;
                    }
                    catch (Exception e) when (e is ObjectDisposedException || e.InnerException is ObjectDisposedException)
                    {
                        // La page a été supprimée pendant la détection en arrière-plan : on l'ignore
                        memoryLimitingSem.Release();
                        lock (this)
                        {
                            Status.CurrentProgress += 1;
                        }
                        InvokeStatusChanged();
                        return null;
                    }
                    try
                    {
                        if (CancelToken.IsCancellationRequested)
                        {
                            return null;
                        }
                        
                      
                        lock (img)
                        {
                            img.BarCodeData = PatchCodeDetector.DetectBarcode(bitmap);
                            if (img.BarCodeData != null)
                            {
                                img.RecoveryIndexImage.BarCode = img.BarCodeData;
                                img.Update(img.BarCodeData);
                            }
                        }
                        if (!string.IsNullOrEmpty(img.BarCodeData))
                        {
                            lock (imagesWithBarcode)
                            {
                                imagesWithBarcode.Add(img);
                            }
                        }

                        // The final pipeline step is pretty fast, so updating progress here is more accurate
                        lock (this)
                        {
                            Status.CurrentProgress += 1;
                        }
                        InvokeStatusChanged();

                        return Tuple.Create(img);
                    }
                    finally
                    {
                        bitmap.Dispose();
                        memoryLimitingSem.Release();
                    }
                }).Run();
                return !CancelToken.IsCancellationRequested;
            });

            return true;
        }
    }
}
