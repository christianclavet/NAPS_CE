using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAPS2.Config;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Images;
using NAPS2.ImportExport.Pdf;
using NAPS2.Lang.Resources;
using NAPS2.Ocr;
using NAPS2.Operation;
using NAPS2.Scan.Images;
using NAPS2.Util;

namespace NAPS2.WinForms
{
    public class WinFormsExportHelper(PdfSettingsContainer pdfSettingsContainer, ImageSettingsContainer imageSettingsContainer, DialogHelper dialogHelper, FileNamePlaceholders fileNamePlaceholders, ChangeTracker changeTracker, IOperationFactory operationFactory, IFormFactory formFactory, OcrManager ocrManager, IOperationProgress operationProgress, IUserConfigManager userConfigManager)
    {
        private readonly PdfSettingsContainer pdfSettingsContainer = pdfSettingsContainer;
        private readonly ImageSettingsContainer imageSettingsContainer = imageSettingsContainer;
        private readonly DialogHelper dialogHelper = dialogHelper;
        private readonly FileNamePlaceholders fileNamePlaceholders = fileNamePlaceholders;
        private readonly ChangeTracker changeTracker = changeTracker;
        private readonly IOperationFactory operationFactory = operationFactory;
        private readonly IFormFactory formFactory = formFactory;
        private readonly OcrManager ocrManager = ocrManager;
        private readonly IOperationProgress operationProgress = operationProgress;
        private readonly IUserConfigManager userConfigManager = userConfigManager;

        public async Task<bool> SavePDF(List<ScannedImage> images, ISaveNotify notify, int doc = 0)
        {
            if (images.Any())
            {
                userConfigManager.Load();
                var lastPath = (userConfigManager.Config.LastPath);
                if(string.IsNullOrEmpty(lastPath))
                    lastPath = "C:\\";
                // In case there is nothing yet in the last path, use the C:\drive
                

                string savePath = Path.Combine(lastPath, FDesktop.projectName);

                if (doc == 0)
                {
                    //if (pdfSettings.SkipSavePrompt && Path.IsPathRooted(pdfSettings.DefaultFileName))
                    //pdfSettings.DefaultFileName
                    
                    string input = savePath;
                        
                   if (!dialogHelper.PromptToSavePdf(input, out savePath))
                   {
                       return false;
                   }
                   else
                   {
                        if (lastPath != Path.GetDirectoryName(savePath))
                        {
                            userConfigManager.Config.LastPath = Path.GetDirectoryName(savePath);
                            userConfigManager.Save();
                        }

                        if (string.Format(MiscResources.ProjectName)==FDesktop.projectName) // Will reset the project name is default and user changed the export name.
                        {
                            FDesktop.projectName = Path.GetFileNameWithoutExtension(savePath);
                        }
                        
                   }
                    
                }

                //append the document number to the file since they are the following documents...
                if (doc > 0)
                {
                    //savePath = savePath + "_" + ((doc + 1).ToString("d4")) + ".pdf";
                    string name = Path.GetFileNameWithoutExtension(images[0].RecoveryIndexImage.SeparatorName);
                    savePath = savePath + "_" + name + ".pdf";
                }
                   


                var changeToken = changeTracker.State;
                string firstFileSaved = await ExportPDF(savePath, images);
                if (firstFileSaved != null)
                {
                    //Don't change the tracker since the project is not saved (backed up)
                    //changeTracker.Saved(changeToken);
                    notify?.PdfSaved(firstFileSaved);
                    return true;
                }
            }
            return false;
        }

        public async Task<string> ExportPDF(string filename, List<ScannedImage> images)
        {
            var op = operationFactory.Create<SavePdfOperation>();

            var pdfSettings = pdfSettingsContainer.PdfSettings;
            pdfSettings.Metadata.Creator = MiscResources.NAPS2;
            if (op.Start(filename, DateTime.Now, images, pdfSettings, ocrManager.DefaultParams))
            {
                operationProgress.ShowProgress(op);
            }
            return await op.Success ? op.FirstFileSaved : null;
        }

        public async Task<bool> SaveImages(List<ScannedImage> images, ISaveNotify notify, bool bypassprompt = false)
        {
            if (images.Any())
            {
                userConfigManager.Load();
                var lastPath = (userConfigManager.Config.LastPath);
                if (string.IsNullOrEmpty(lastPath))
                    lastPath = "C:\\";
                // In case there is nothing yet in the last path, use the C:\drive

                string savePath = Path.Combine(lastPath, FDesktop.projectName);

                var imageSettings = imageSettingsContainer.ImageSettings;
                if (imageSettings.SkipSavePrompt && Path.IsPathRooted(imageSettings.DefaultFileName))
                {
                    if (!(imageSettings.DefaultFileName == ""))
                        savePath = imageSettings.DefaultFileName;
                    else
                        savePath = savePath + FDesktop.projectName;
                }
                else if (!dialogHelper.PromptToSaveImage(savePath, out savePath))
                {
                   return false;
                }
              
                var op = operationFactory.Create<SaveImagesOperation>();
                var changeToken = changeTracker.State;
                
                if (op.Start(savePath, DateTime.Now, images))
                {
                    operationProgress.ShowProgress(op);
                }
                if (await op.Success)
                {
                    //Don't change the tracker since the project is not saved (backed up)
                    //changeTracker.Saved(changeToken);
                    notify?.ImagesSaved(images.Count, op.FirstFileSaved);
                    return true;
                }
            }
            return false;
        }
        public async Task<bool> SaveProjectImages(List<ScannedImage> images, ISaveNotify notify, bool bypassprompt = false)
        {
            if (images.Any())
            {

                userConfigManager.Load();
                string savePath = Path.Combine(userConfigManager.Config.LastPath, FDesktop.projectName);

                var imageSettings = imageSettingsContainer.ImageSettings;
                //ImageSettingsContainer.ProjectSettings.UseCSVExport == false
                if (Path.IsPathRooted(ImageSettingsContainer.ProjectSettings.DefaultFileName))
                {
                     savePath = ImageSettingsContainer.ProjectSettings.DefaultFileName;
                }
                else if (!dialogHelper.PromptToSaveImage(ImageSettingsContainer.ProjectSettings.DefaultFileName, out savePath))
                {
                    return false;
                }
                
                var op = operationFactory.Create<SaveImagesOperationProject>();
                var changeToken = changeTracker.State;
                op.imageSettings = imageSettings;
                if (op.Start(savePath, DateTime.Now, images))
                {
                    operationProgress.ShowProgress(op);
                }
                if (await op.Success)
                {
                    //Don't change the tracker since the project is not saved (backed up)
                    //changeTracker.Saved(changeToken);
                    notify?.ImagesSaved(images.Count, op.FirstFileSaved);
                    return true;
                }
            }
            return false;
        }
    }
}
