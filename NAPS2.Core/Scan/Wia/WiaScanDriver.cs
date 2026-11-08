using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAPS2.Logging;
using NAPS2.Operation;
using NAPS2.Platform;
using NAPS2.Scan.Exceptions;
using NAPS2.Scan.Images;
using NAPS2.Scan.Wia.Native;
using NAPS2.Util;
using NAPS2.WinForms;

namespace NAPS2.Scan.Wia
{
    public class WiaScanDriver : ScanDriverBase
    {
        public const string DRIVER_NAME = "wia";

        private readonly IOperationFactory operationFactory;
        private readonly IOperationProgress operationProgress;

        public WiaScanDriver(IFormFactory formFactory, IOperationFactory operationFactory, IOperationProgress operationProgress)
            : base(formFactory)
        {
            this.operationFactory = operationFactory;
            this.operationProgress = operationProgress;
        }

        public override string DriverName => DRIVER_NAME;

        public override string ReturnCaps()
        {
            if (ScanDevice == null)
            {
                return "No WIA device selected";
            }
            try
            {
                using (var deviceManager = new WiaDeviceManager(ScanProfile.WiaVersion))
                using (var device = deviceManager.FindDevice(ScanDevice.ID))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Name:  " + ScanDevice.Name);
                    sb.AppendLine();
                    sb.AppendLine("CURRENT CAPABILITIES:");
                    AppendProperties(sb, device.Properties);
                    return sb.ToString();
                }
            }
            catch (Exception e)
            {
                Log.ErrorException("Error reading WIA capabilities", e);
                return "Unable to read WIA capabilities: " + e.Message;
            }
        }

        private static void AppendProperties(StringBuilder sb, WiaPropertyCollection properties)
        {
            foreach (var prop in properties.OrderBy(x => x.Name))
            {
                // WiaProperty.Value only supports I4 and BSTR properties
                if (prop.Type != WiaPropertyType.I4 && prop.Type != WiaPropertyType.BSTR)
                {
                    continue;
                }
                try
                {
                    sb.AppendLine(prop.Name + ": " + prop.Value);
                    var attributes = prop.Attributes;
                    if (attributes.Flags.HasFlag(WiaPropertyFlags.Range))
                    {
                        sb.AppendLine("    Range: " + attributes.Min + " to " + attributes.Max + " (step " + attributes.Step + ")");
                    }
                    else if (attributes.Flags.HasFlag(WiaPropertyFlags.List) && attributes.Values != null)
                    {
                        sb.AppendLine("    Choices: " + string.Join(", ", attributes.Values));
                    }
                }
                catch (Exception e)
                {
                    Log.ErrorException("Error reading WIA property " + prop.Name, e);
                }
            }
        }

        public override bool IsSupported => PlatformCompat.System.IsWiaDriverSupported;

        protected override ScanDevice PromptForDeviceInternal()
        {
            try
            {
                using (var deviceManager = new WiaDeviceManager(ScanProfile.WiaVersion))
                {
                    using (var device = deviceManager.PromptForDevice(DialogParent?.Handle ?? IntPtr.Zero))
                    {
                        if (device == null)
                        {
                            return null;
                        }

                        return new ScanDevice(device.Id(), device.Name());
                    }
                }
            }
            catch (WiaException e) when (e.ErrorCode == WiaErrorCodes.NO_DEVICE_AVAILABLE)
            {
                throw new NoDevicesFoundException();
            }
        }

        protected override List<ScanDevice> GetDeviceListInternal()
        {
            using (var deviceManager = new WiaDeviceManager(ScanProfile.WiaVersion))
            {
                return deviceManager.GetDeviceInfos().Select(deviceInfo =>
                {
                    using (deviceInfo)
                    {
                        return new ScanDevice(deviceInfo.Id(), deviceInfo.Name());
                    }
                }).ToList();
            }
        }

        protected override async Task ScanInternal(ScannedImageSource.Concrete source)
        {
            var op = operationFactory.Create<WiaScanOperation>();
            using (CancelToken.Register(op.Cancel))
            {
                op.Start(ScanProfile, ScanDevice, ScanParams, DialogParent, source);
                Invoker.Current.SafeInvoke(() =>
                {
                    if (ScanParams.Modal && !ScanParams.NoUI)
                    {
                        operationProgress.ShowModalProgress(op);
                    }
                    else
                    {
                        operationProgress.ShowBackgroundProgress(op);
                    }
                });
                await op.Success;
            }

            if (op.ScanException != null)
            {
                op.ScanException.PreserveStackTrace();
                throw op.ScanException;
            }
        }
    }
}
