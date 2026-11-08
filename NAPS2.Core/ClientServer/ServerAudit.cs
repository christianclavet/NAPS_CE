using System;
using System.IO;
using System.ServiceModel;
using System.ServiceModel.Channels;
using NAPS2.Util;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace NAPS2.ClientServer
{
    /// <summary>
    /// Writes a dedicated audit log of every client that connects to the NAPS2 server
    /// (scanning server mode). Clients that do not declare NAPS2 CriCri Edition are flagged.
    ///
    /// Note: the client declares its own identity, so this log helps detect legitimate
    /// clients that are misconfigured or outdated. It is not an authentication mechanism.
    /// </summary>
    public static class ServerAudit
    {
        private static readonly Lazy<Logger> Logger = new Lazy<Logger>(CreateLogger);

        private static Logger CreateLogger()
        {
            var config = new LoggingConfiguration();
            var target = new FileTarget
            {
                FileName = Path.Combine(Paths.AppData, "server-log.txt"),
                Layout = "${longdate} ${level:uppercase=true} ${message}",
                ArchiveAboveSize = 1000000,
                MaxArchiveFiles = 5
            };
            config.AddTarget("serverauditfile", target);
            config.LoggingRules.Add(new LoggingRule("NAPS2.ServerAudit", LogLevel.Info, target));
            var logFactory = new LogFactory(config);
            return logFactory.GetLogger("NAPS2.ServerAudit");
        }

        public static void LogClientConnection(string clientName, string clientVersion)
        {
            try
            {
                string remote = GetRemoteAddress();
                if (clientName == ClientIdentity.EXPECTED_PRODUCT)
                {
                    Logger.Value.Info("Client connected: {0} version {1} from {2}", clientName, clientVersion, remote);
                }
                else
                {
                    Logger.Value.Warn("UNKNOWN CLIENT connected: declared name '{0}' version {1} from {2}. " +
                                      "This is not a NAPS2 CriCri Edition client.",
                        clientName, clientVersion, remote);
                }
            }
            catch (Exception)
            {
                // Auditing must never break scanning
            }
        }

        private static string GetRemoteAddress()
        {
            var context = OperationContext.Current;
            if (context == null)
            {
                return "unknown";
            }
            var prop = context.IncomingMessageProperties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
            return prop != null ? $"{prop.Address}:{prop.Port}" : "unknown";
        }
    }
}
