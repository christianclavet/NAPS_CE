using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.Platform;
using NAPS2.Recovery;
using NAPS2.Scan;
using NAPS2.Scan.Sane;
using NAPS2.Scan.Twain;
using NAPS2.Scan.Wia;

namespace NAPS2.ClientServer
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession,
        IncludeExceptionDetailInFaults = true,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ScanService : IScanService
    {
        private readonly IScanDriverFactory scanDriverFactory;
        private readonly IScanPerformer scanPerformer;

        private CancellationTokenSource scanCts = new CancellationTokenSource();

        public ScanService(IScanDriverFactory scanDriverFactory, IScanPerformer scanPerformer)
        {
            this.scanDriverFactory = scanDriverFactory;
            this.scanPerformer = scanPerformer;
        }

        public void Hello(string clientName, string clientVersion)
        {
            RequireLocalNetwork();
            ServerAudit.LogClientConnection(clientName, clientVersion);
        }

        /// <summary>
        /// Rejects requests from outside the local network. Every operation calls this first.
        /// </summary>
        private static void RequireLocalNetwork()
        {
            var remote = ServerAddressFilter.CurrentRemoteAddress();
            if (!ServerAddressFilter.IsAllowed(remote))
            {
                ServerAudit.LogRejected(remote);
                throw new System.ServiceModel.FaultException("Connection not allowed from this network.");
            }
        }

        public List<string> GetSupportedDriverNames()
        {
            RequireLocalNetwork();
            var driverNames = new List<string>();
            if (PlatformCompat.System.IsWiaDriverSupported)
            {
                driverNames.Add(WiaScanDriver.DRIVER_NAME);
            }

            if (PlatformCompat.System.IsTwainDriverSupported)
            {
                driverNames.Add(TwainScanDriver.DRIVER_NAME);
            }

            if (PlatformCompat.System.IsSaneDriverSupported)
            {
                driverNames.Add(SaneScanDriver.DRIVER_NAME);
            }

            return driverNames;
        }

        public List<ScanDevice> GetDeviceList(ScanProfile scanProfile)
        {
            RequireLocalNetwork();
            var profile = BuildServerProfile(scanProfile);
            var driver = scanDriverFactory.Create(profile.DriverName);
            driver.ScanProfile = profile;
            return driver.GetDeviceList();
        }

        /// <summary>
        /// Builds the profile used on the server. The client's driver choice is only honored
        /// if it is one of the drivers this server supports locally; otherwise the server default is used.
        /// </summary>
        private ScanProfile BuildServerProfile(ScanProfile requested)
        {
            var supported = GetSupportedDriverNames();
            string driverName = supported.FirstOrDefault(x => x == requested?.DriverName)
                                ?? supported.FirstOrDefault();
            if (driverName == null)
            {
                throw new InvalidOperationException("No scanning driver is available on this server.");
            }
            return ScanProfileSanitizer.Sanitize(requested, driverName);
        }

        public async Task<int> Scan(ScanProfile scanProfile, ScanParams scanParams)
        {
            RequireLocalNetwork();
            scanProfile = BuildServerProfile(scanProfile);
            if (scanProfile.TwainImpl == TwainImpl.Legacy)
            {
                scanProfile.TwainImpl = TwainImpl.OldDsm;
            }
            scanProfile.UseNativeUI = false;

            var internalParams = new ScanParams
            {
                DetectPatchCodes = scanParams.DetectPatchCodes,
                NoUI = true,
                NoAutoSave = true,
                DoOcr = false,
                NoThumbnails = true,
                SkipPostProcessing = true
            };

            var callback = OperationContext.Current.GetCallbackChannel<IScanCallback>();

            int pages = 0;
            await scanPerformer.PerformScan(scanProfile, internalParams, null, image =>
            {
                // TODO: Should stream this
                // TODO: Also should think about avoiding the intermediate filesystem
                using (image)
                {
                    var indexImage = image.RecoveryIndexImage;
                    var imageBytes = File.ReadAllBytes(image.RecoveryFilePath);
                    var sanitizedIndexImage = new RecoveryIndexImage
                    {
                        FileName = Path.GetExtension(indexImage.FileName),
                        TransformList = indexImage.TransformList,
                        BitDepth = indexImage.BitDepth,
                        HighQuality = indexImage.HighQuality,
                        SheetSide = indexImage.SheetSide,
                        BarCode = indexImage.BarCode,
                    };
                    callback.ImageReceived(imageBytes, sanitizedIndexImage);
                    pages++;
                }
            }, scanCts.Token);
            return pages;
        }

        public void CancelScan()
        {
            RequireLocalNetwork();
            scanCts.Cancel();
        }
    }
}
