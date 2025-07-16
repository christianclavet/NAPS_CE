using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.Scan.Images;
using NAPS2.Util;
using ImageMagick;
using System.Windows.Forms;
using NTwain.Data;
using static System.Net.Mime.MediaTypeNames;

namespace NAPS2.ImportExport.Images
{
    public class TiffHelper
    {
        private readonly ScannedImageRenderer scannedImageRenderer;
        private readonly ImageSettingsContainer imageSettingsContainer;

        public TiffHelper(ScannedImageRenderer scannedImageRenderer, ImageSettingsContainer imageSettingsContainer)
        {
            this.scannedImageRenderer = scannedImageRenderer;
            this.imageSettingsContainer = imageSettingsContainer;
        }

        public async Task<bool> SaveMultipage(List<ScannedImage.Snapshot> snapshots, string location, TiffCompression compression, ProgressHandler progressCallback, CancellationToken cancelToken)
        {
            try
            {
                var m = new MagickFactory();
                ImageCodecInfo codecInfo = GetCodecForString("TIFF");

                progressCallback(0, snapshots.Count);
                if (cancelToken.IsCancellationRequested)
                {
                    return false;
                }

                PathHelper.EnsureParentDirExists(location);

                if (snapshots.Count == 1)
                {
                    var iparams = new EncoderParameters(1);
                    Encoder iparam = Encoder.Compression;
                    using (var bitmap = await scannedImageRenderer.Render(snapshots[0]))
                    {
                        ValidateBitmap(bitmap);
                        //
                        //
                        // -- OLD METHOD USING GDI+ -- , NEW METHOD USES ImageMagick
                        //var iparamPara = new EncoderParameter(iparam, (long)GetEncoderValue(compression, bitmap));
                        //iparams.Param[0] = iparamPara;
                        //bitmap.Save(location, codecInfo, iparams);


                        //Getting the scannedImageRenderer will apply all the transforms on the picture.
                        using (MagickImage image = new MagickImage(m.Image.Create(bitmap)))
                        {
                            image.Settings.Depth = 24;
                            if (compression == TiffCompression.Jpeg)
                                image.Settings.Compression = CompressionMethod.JPEG;

                            if (compression == TiffCompression.Lzw)
                                image.Settings.Compression = CompressionMethod.LZW;

                            if (compression == TiffCompression.Auto)
                                image.Settings.Compression = CompressionMethod.Undefined;

                            if (compression == TiffCompression.Ccitt4)
                            {
                                image.Settings.Compression = CompressionMethod.Group4;
                                image.Settings.Depth = 1; // CCITT4 is always 1 bit per pixel
                            }

                            if (compression == TiffCompression.None)
                                image.Settings.Compression = CompressionMethod.NoCompression;

                            var quality = imageSettingsContainer.ImageSettings.JpegQuality;
                            if (compression == TiffCompression.Jpeg && quality > 0)
                            {
                                image.Quality = quality;
                            }

                            //image.Density = new Density(300, 300);
                            image.Format = MagickFormat.Tiff;
                            // Save frame as tiff with JPG compression method.

                            image.Write(location);
                        }
                    }
                }
                else if (snapshots.Count > 1)
                {
                    var encoderParams = new EncoderParameters(2);
                    var saveEncoder = Encoder.SaveFlag;
                    var compressionEncoder = Encoder.Compression;

                    File.Delete(location);
                    using (var bitmap0 = await scannedImageRenderer.Render(snapshots[0]))
                    {
                        var album = new MagickImageCollection();
                        ValidateBitmap(bitmap0);

                        using (MagickImage image = new MagickImage(m.Image.Create(bitmap0)))
                        {
                            for (int i = 1; i < snapshots.Count; i++)
                            {
                                if (snapshots[i] == null)
                                    break;

                                progressCallback(i, snapshots.Count);
                                if (cancelToken.IsCancellationRequested)
                                {
                                    bitmap0.Dispose();
                                    File.Delete(location);
                                    album.Dispose();
                                    return false;
                                }

                                using (var bitmap = await scannedImageRenderer.Render(snapshots[i]))
                                {
                                    ValidateBitmap(bitmap);
                                    MagickImage img = new MagickImage(m.Image.Create(bitmap));
                                    var quality = imageSettingsContainer.ImageSettings.JpegQuality;
                                    if (compression == TiffCompression.Jpeg && quality > 0)
                                    {
                                        img.Quality = quality;
                                    }

                                    if (compression == TiffCompression.Jpeg)
                                        img.Settings.Compression = CompressionMethod.JPEG;

                                    if (compression == TiffCompression.Lzw)
                                        img.Settings.Compression = CompressionMethod.LZW;

                                    if (compression == TiffCompression.Auto)
                                        img.Settings.Compression = CompressionMethod.Undefined;

                                    if (compression == TiffCompression.Ccitt4)
                                    {
                                        img.Settings.Compression = CompressionMethod.Group4;
                                        img.Settings.Depth = 1; // CCITT4 is always 1 bit per pixel
                                    }

                                    if (compression == TiffCompression.None)
                                        img.Settings.Compression = CompressionMethod.NoCompression;           


                                    album.Add(img.Clone());
                                    
                                }

                            }

                        }
                        album.Write(location);
                    }

                }

                return true;

            }
            catch (Exception ex)
            {
                throw new Exception("Error saving TIFF", ex);
            }

        }

        private void ValidateBitmap(Bitmap bitmap)
        {
            if (bitmap.PixelFormat == PixelFormat.Format1bppIndexed
                && bitmap.Palette.Entries.Length == 2
                && bitmap.Palette.Entries[0].ToArgb() == Color.White.ToArgb()
                && bitmap.Palette.Entries[1].ToArgb() == Color.Black.ToArgb())
            {
                // Inverted palette (0 = white); some scanners may produce bitmaps like this
                // It won't encode properly in a TIFF, so we need to invert the encoding
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                var stride = Math.Abs(data.Stride);
                for (int y = 0; y < data.Height; y++)
                {
                    for (int x = 0; x < data.Width; x += 8)
                    {
                        byte b = Marshal.ReadByte(data.Scan0 + y * stride + x / 8);
                        int bits = Math.Min(8, data.Width - x);
                        b ^= (byte)(0xFF << (8 - bits));
                        Marshal.WriteByte(data.Scan0 + y * stride + x / 8, b);
                    }
                }
                bitmap.UnlockBits(data);
                bitmap.Palette.Entries[0] = Color.Black;
                bitmap.Palette.Entries[1] = Color.White;
            }
        }

        private ImageCodecInfo GetCodecForString(string type)
        {
            ImageCodecInfo[] info = ImageCodecInfo.GetImageEncoders();
            return info.FirstOrDefault(t => t.FormatDescription.Equals(type));
        }
    }
}
