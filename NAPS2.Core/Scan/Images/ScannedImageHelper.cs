using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAPS2.Config;
using NAPS2.Ocr;
using NAPS2.Operation;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Util;
using ImageMagick;
using System.Windows.Forms;
using ImageMagick.Formats;
using NAPS2.ImportExport.Images;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using NAPS2.WinForms;
using NTwain.Data;

namespace NAPS2.Scan.Images
{
    public class ScannedImageHelper
    {
    
        /// <summary>
        /// Chooses the recovery file format from the scan settings. Nothing is encoded here, so this is cheap
        /// and can run on the scan thread.
        /// PNG (lossless) is used for high quality, black and white, and 1-bit images.
        /// JPEG (lossy) is used for everything else. The profile decides; no size comparison is made.
        /// </summary>
        public static ImageFormat GetRecoveryFormat(Bitmap bitmap, ScanBitDepth bitDepth, bool highQuality)
        {
            if (highQuality || bitDepth == ScanBitDepth.BlackWhite || bitmap.PixelFormat == PixelFormat.Format1bppIndexed)
            {
                return ImageFormat.Png;
            }
            return ImageFormat.Jpeg;
        }

        /// <summary>
        /// Writes a bitmap to <paramref name="path"/> in the format given by <see cref="GetRecoveryFormat"/>.
        /// A single encode is performed; the output is not compared against another format.
        /// Black and white scans are converted to a 1-bit PNG first, which is lossless and very compact.
        /// This is slow, so callers should run it on a background thread.
        /// </summary>
        public static void EncodeForRecovery(Bitmap bitmap, ImageFormat format, ScanBitDepth bitDepth, int quality, string path)
        {
            if (Equals(format, ImageFormat.Png))
            {
                if (bitDepth == ScanBitDepth.BlackWhite && bitmap.PixelFormat != PixelFormat.Format1bppIndexed)
                {
                    using (var bw = BitmapHelper.CopyToBpp(bitmap, 1))
                    {
                        bw.Save(path, ImageFormat.Png);
                    }
                }
                else
                {
                    bitmap.Save(path, ImageFormat.Png);
                }
                return;
            }
            EncodeJpeg(bitmap, quality, path);
        }

        private static void EncodeJpeg(Bitmap bitmap, int quality, string path)
        {
            if (quality == -1)
            {
                bitmap.Save(path, ImageFormat.Jpeg);
            }
            else
            {
                quality = Math.Max(Math.Min(quality, 100), 0);
                var encoder = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bitmap.Save(path, encoder, encoderParams);
            }
        }

        public static ScannedImageHelper getInstance()
        {
            return instance;
        }

        private static ScannedImageHelper instance;
        private readonly ThumbnailRenderer thumbnailRenderer;
        private readonly ScannedImageRenderer scannedImageRenderer;
        private readonly IOperationFactory operationFactory;
        private readonly IOperationProgress operationProgress;
        private readonly AppConfigManager appConfigManager;
        private readonly IUserConfigManager userConfigManager;
        private readonly OcrRequestQueue ocrRequestQueue;
        private readonly OcrManager ocrManager;

        public ScannedImageHelper(ThumbnailRenderer thumbnailRenderer, ScannedImageRenderer scannedImageRenderer, IOperationFactory operationFactory, IOperationProgress operationProgress, AppConfigManager appConfigManager, IUserConfigManager userConfigManager, OcrRequestQueue ocrRequestQueue, OcrManager ocrManager)
        {
            this.thumbnailRenderer = thumbnailRenderer;
            this.scannedImageRenderer = scannedImageRenderer;
            this.operationFactory = operationFactory;
            this.operationProgress = operationProgress;
            this.appConfigManager = appConfigManager;
            this.userConfigManager = userConfigManager;
            this.ocrRequestQueue = ocrRequestQueue;
            this.ocrManager = ocrManager;
            
            instance = this;
        }

        public Bitmap PostProcessStep1(Image output, ScanProfile profile, bool supportsNativeUI = true)
        {
            double scaleFactor = 1;
            if (!profile.UseNativeUI || !supportsNativeUI)
            {
                scaleFactor = profile.AfterScanScale.ToIntScaleFactor();
            }
            var result = ImageScaleHelper.ScaleImage(output, scaleFactor);

            if ((!profile.UseNativeUI || !supportsNativeUI) && (profile.ForcePageSize || profile.ForcePageSizeCrop))
            {
                float width = output.Width / output.HorizontalResolution;
                float height = output.Height / output.VerticalResolution;
                if (float.IsNaN(width) || float.IsNaN(height))
                {
                    width = output.Width;
                    height = output.Height;
                }
                PageDimensions pageDimensions = profile.PageSize.PageDimensions() ?? profile.CustomPageSize;
                if (pageDimensions.Width > pageDimensions.Height && width < height)
                {
                    if (profile.ForcePageSizeCrop)
                    {
                        result = new CropTransform
                        {
                            Right = (int) ((width - (float) pageDimensions.HeightInInches()) * output.HorizontalResolution),
                            Bottom = (int) ((height - (float) pageDimensions.WidthInInches()) * output.VerticalResolution)
                        }.Perform(result);
                    }
                    else
                    {
                        result.SafeSetResolution((float) (output.Width / pageDimensions.HeightInInches()),
                            (float) (output.Height / pageDimensions.WidthInInches()));
                    }
                }
                else
                {
                    if (profile.ForcePageSizeCrop)
                    {
                        result = new CropTransform
                        {
                            Right = (int) ((width - (float) pageDimensions.WidthInInches()) * output.HorizontalResolution),
                            Bottom = (int) ((height - (float) pageDimensions.HeightInInches()) * output.VerticalResolution)
                        }.Perform(result);
                    }
                    else
                    {
                        result.SafeSetResolution((float)(output.Width / pageDimensions.WidthInInches()), (float)(output.Height / pageDimensions.HeightInInches()));
                    }
                }
            }
            return result;
        }

        public void PostProcessStep2(ScannedImage image, Bitmap bitmap, ScanProfile profile, ScanParams scanParams, int pageNumber, bool supportsNativeUI = true)
        {
            if (!scanParams.NoThumbnails)
            {
                image.SetThumbnail(thumbnailRenderer.RenderThumbnail(bitmap));
            }
            if (scanParams.SkipPostProcessing)
            {
                return;
            }
            if ((!profile.UseNativeUI || !supportsNativeUI) && profile.BrightnessContrastAfterScan)
            {
                if (profile.Brightness != 0)
                {
                    AddTransformAndUpdateThumbnail(image, ref bitmap, new BrightnessTransform { Brightness = profile.Brightness });
                }
                if (profile.Contrast != 0)
                {
                    AddTransformAndUpdateThumbnail(image, ref bitmap, new TrueContrastTransform { Contrast = profile.Contrast });
                }
            }
            if (profile.FlipDuplexedPages && pageNumber % 2 == 0)
            {
                AddTransformAndUpdateThumbnail(image, ref bitmap, new RotationTransform(RotateFlipType.Rotate180FlipNone));
            }
            if (profile.AutoDeskew)
            {
                var op = operationFactory.Create<DeskewOperation>();
                if (op.Start(new[] { image }))
                {
                    operationProgress.ShowProgress(op);
                    op.Wait();
                }
            }
            if (scanParams.DetectPatchCodes && image.PatchCode == PatchCode.None)
            {
                image.PatchCode = PatchCodeDetector.Detect(bitmap);
            }
        }

        public bool ShouldDoBackgroundOcr(ScanParams scanParams)
        {
            bool ocrEnabled = ocrManager.DefaultParams != null;
            bool afterScanning = appConfigManager.Config.OcrState == OcrState.Enabled && appConfigManager.Config.OcrDefaultAfterScanning
                                 || appConfigManager.Config.OcrState == OcrState.UserConfig &&
                                 (userConfigManager.Config.OcrAfterScanning ?? appConfigManager.Config.OcrDefaultAfterScanning);
            return scanParams.DoOcr ?? (ocrEnabled && afterScanning);
        }

        public string SaveForBackgroundOcr(Bitmap bitmap, ScanParams scanParams, bool force = false)
        {
            if (ShouldDoBackgroundOcr(scanParams) || force)
            {
                string tempPath = Path.Combine(Paths.Temp, Path.GetRandomFileName());
                bitmap.Save(tempPath);
                return tempPath;
            }
            return null;
        }

        public void RunBackgroundOcr(ScannedImage image, ScanParams scanParams, string tempPath, bool force = false)
        {
            if (ShouldDoBackgroundOcr(scanParams) || force)
            {
                using (var snapshot = image.Preserve())
                {
                    if (scanParams.DoOcr == true)
                    {
                        ocrRequestQueue.QueueForeground(null, snapshot, tempPath, scanParams.OcrParams, scanParams.OcrCancelToken).AssertNoAwait();
                    }
                    else
                    {
                        ocrRequestQueue.QueueBackground(snapshot, tempPath, scanParams.OcrParams);
                    }
                }
            }
        }

        public void CancelBackgroundOcr()
        {
            ocrRequestQueue.CancelBackground();
        }

        public bool IsOcrBusy => ocrRequestQueue.IsBusy;

        public event EventHandler OcrBusyChanged
        {
            add => ocrRequestQueue.OcrBusyChanged += value;
            remove => ocrRequestQueue.OcrBusyChanged -= value;
        }

        private void AddTransformAndUpdateThumbnail(ScannedImage image, ref Bitmap bitmap, Transform transform)
        {
            image.AddTransform(transform);
            var thumbnail = image.GetThumbnail();
            if (thumbnail != null)
            {
                bitmap = transform.Perform(bitmap);
                image.SetThumbnail(thumbnailRenderer.RenderThumbnail(bitmap));
            }
        }
        
    }
}
