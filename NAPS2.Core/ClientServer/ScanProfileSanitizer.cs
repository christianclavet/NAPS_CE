using System;
using NAPS2.Scan;

namespace NAPS2.ClientServer
{
    /// <summary>
    /// Builds the scan profile that the server actually uses, from the client's request.
    /// Only scanning settings are copied. Anything that controls files, network connections,
    /// drivers or external components is ignored, so a connected client cannot make the server
    /// write files, connect elsewhere, or load a driver of its choice.
    /// </summary>
    public static class ScanProfileSanitizer
    {
        /// <summary>
        /// Returns a new profile containing only the client's scanning settings.
        /// The server's own driver and device are used; the caller sets them.
        /// </summary>
        public static ScanProfile Sanitize(ScanProfile request, string serverDriverName)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return new ScanProfile
            {
                // Chosen by the server, never by the client
                DriverName = serverDriverName,
                Device = request.Device,
                TwainImpl = request.TwainImpl,
                UseNativeUI = false,

                // Scanning settings
                PaperSource = request.PaperSource,
                BitDepth = request.BitDepth,
                Resolution = request.Resolution,
                PageSize = request.PageSize,
                CustomPageSizeName = request.CustomPageSizeName,
                CustomPageSize = request.CustomPageSize,
                PageAlign = request.PageAlign,
                AfterScanScale = request.AfterScanScale,
                MaxQuality = request.MaxQuality,
                Quality = request.Quality,
                Brightness = request.Brightness,
                Contrast = request.Contrast,
                AutoDeskew = request.AutoDeskew,
                ExcludeBlankPages = request.ExcludeBlankPages,
                BlankPageWhiteThreshold = request.BlankPageWhiteThreshold,
                BlankPageCoverageThreshold = request.BlankPageCoverageThreshold,
                WiaVersion = request.WiaVersion,
                WiaDelayBetweenScans = request.WiaDelayBetweenScans,
                WiaDelayBetweenScansSeconds = request.WiaDelayBetweenScansSeconds,
                WiaOffsetWidth = request.WiaOffsetWidth,
                WiaRetryOnFailure = request.WiaRetryOnFailure,
                FlipDuplexedPages = request.FlipDuplexedPages,
                AutoPageRotation = request.AutoPageRotation,
                AutoPageDeskew = request.AutoPageDeskew,
                DoubleFeedType = request.DoubleFeedType,
                DoubleFeedAction = request.DoubleFeedAction,
                DoubleFeedSensivity = request.DoubleFeedSensivity,
                PaperType = request.PaperType,
                AutoBorderDetection = request.AutoBorderDetection,

                // Deliberately NOT copied:
                // - AutoSaveSettings, EnableAutoSave (file writes on the server)
                // - ProxyConfig, ProxyDriverName (outbound connections)
                // - DriverName from the client (driver choice)
                // - IsDefault, IsLocked, IconID, DisplayName, Capabilities, etc. (UI/state only)
                EnableAutoSave = false,
                AutoSaveSettings = null,
                ProxyConfig = null,
                ProxyDriverName = null,
                KeyValueOptions = null,
                Version = ScanProfile.CURRENT_VERSION,
            };
        }
    }
}
