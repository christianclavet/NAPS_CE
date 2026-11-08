using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAPS2.Lang.Resources;
using NAPS2.Logging;
using NAPS2.Ocr;
using NAPS2.Operation;
using NAPS2.Scan.Images;
using NAPS2.Util;

namespace NAPS2.ImportExport.Pdf
{
    public class SavePdfOperation : OperationBase
    {
        private readonly FileNamePlaceholders fileNamePlaceholders;
        private readonly IPdfExporter pdfExporter;
        private readonly IOverwritePrompt overwritePrompt;
        public SavePdfOperation(FileNamePlaceholders fileNamePlaceholders, IPdfExporter pdfExporter, IOverwritePrompt overwritePrompt)
        {
            this.fileNamePlaceholders = fileNamePlaceholders;
            this.pdfExporter = pdfExporter;
            this.overwritePrompt = overwritePrompt;

            AllowCancel = true;
            AllowBackground = false;
        }

        public string FirstFileSaved { get; private set; }

        public bool Start(string fileName, DateTime dateTime, ICollection<ScannedImage> images, PdfSettings pdfSettings, OcrParams ocrParams)
        {
            ProgressTitle = MiscResources.SavePdfProgress;
            Status = new OperationStatus
            {
                MaxProgress = images.Count
            };

            if (Directory.Exists(fileNamePlaceholders.SubstitutePlaceholders(fileName, dateTime)))
            {
                // Not supposed to be a directory, but ok...
                fileName = Path.Combine(fileName, "$(n).pdf");
            }

            var singleFile = !pdfSettings.SinglePagePdf || images.Count == 1;
            var subFileName = fileNamePlaceholders.SubstitutePlaceholders(fileName, dateTime);
            if (singleFile)
            {
                if (File.Exists(subFileName) && overwritePrompt.ConfirmOverwrite(subFileName) != DialogResult.Yes)
                {
                    return false;
                }
            }

            var snapshots = images.Select(x => x.Preserve()).ToList();
            var snapshotsByFile = pdfSettings.SinglePagePdf ? snapshots.Select(x => new[] { x }).ToArray() : new[] { snapshots.ToArray() };
            RunAsync(async () =>
            {
                bool result = false;
                try
                {
                    int digits = (int)Math.Floor(Math.Log10(snapshots.Count)) + 1;
                    int i = 0;
                    foreach (var snapshotArray in snapshotsByFile)
                    {
                        subFileName = fileNamePlaceholders.SubstitutePlaceholders(fileName, dateTime, true, i, singleFile ? 0 : digits);
                        Status.StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(subFileName));
                        InvokeStatusChanged();
                        if (singleFile && IsFileInUse(subFileName, out var ex))
                        {
                            InvokeError(MiscResources.FileInUse, ex);
                            break;
                        }

                        var progress = singleFile ? OnProgress : (ProgressHandler)((j, k) => { });
                        
                        // Should check for multi document in here a create a loop here.
                        result = await pdfExporter.Export(subFileName, snapshotArray, pdfSettings, ocrParams, progress, CancelToken);


                        if (!result || CancelToken.IsCancellationRequested)
                        {
                            break;
                        }
                        if (i == 0)
                        {
                            FirstFileSaved = subFileName;
                        }
                        i++;
                        if (!singleFile)
                        {
                            OnProgress(i, snapshotsByFile.Length);
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    InvokeError(MiscResources.DontHavePermission, ex);
                }
                catch (IOException ex)
                {
                    if (File.Exists(subFileName))
                    {
                        InvokeError(MiscResources.FileInUse, ex);
                    }
                    else
                    {
                        Log.ErrorException(MiscResources.ErrorSaving, ex);
                        InvokeError(MiscResources.ErrorSaving, ex);
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorException(MiscResources.ErrorSaving, ex);
                    InvokeError(MiscResources.ErrorSaving, ex);
                }
                finally
                {
                    snapshots.ForEach(s => s.Dispose());
                    GC.Collect();
                }

                return result;
            });
            Success.ContinueWith(task =>
            {
                if (task.Result)
                {
                    Log.Event(EventType.SavePdf, new Event
                    {
                        Name = MiscResources.SavePdf,
                        Pages = snapshots.Count,
                        FileFormat = ".pdf"
                    });
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            return true;
        }

        private bool IsFileInUse(string filePath, out Exception exception)
        {
            // TODO: Generalize this for images too
            exception = null;
            if (File.Exists(filePath))
            {
                try
                {
                    using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
                catch (IOException ex)
                {
                    exception = ex;
                    return true;
                }
            }
            return false;
        }
    }
}
