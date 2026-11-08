using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using NAPS2.Config;
using NAPS2.Logging;
using NAPS2.Scan;

namespace NAPS2.ClientServer
{
    public class ClientContextFactory
    {
        private readonly IUserConfigManager userConfigManager;

        public ClientContextFactory(IUserConfigManager userConfigManager)
        {
            this.userConfigManager = userConfigManager;
        }

        public ClientContext Create(ScanProxyConfig proxyConfig)
        {
            // Only private network addresses are accepted, matching the server's own restriction
            if (!IPAddress.TryParse(proxyConfig.Ip ?? "", out var ip) || !ServerAddressFilter.IsAllowed(ip))
            {
                throw new InvalidOperationException("The scanning server address must be on the local network.");
            }
            var key = $"{proxyConfig.Ip}:{proxyConfig.Port}";
            var uri = new Uri($"net.tcp://{proxyConfig.Ip}:{proxyConfig.Port}/NAPS2.Server");
            var callback = new ScanCallback();
            var instanceContext = new InstanceContext(callback);
            var binding = new NetTcpBinding
            {
                MaxReceivedMessageSize = 64 * 1024 * 1024,
                Security = { Mode = SecurityMode.Transport, Transport = { ClientCredentialType = TcpClientCredentialType.None } }
            };
            var channelFactory = new DuplexChannelFactory<IScanService>(instanceContext, binding, new EndpointAddress(uri));
            var channel = channelFactory.CreateChannel();
            try
            {
                VerifyPinnedCertificate(channel, key);
                channel.Hello(ClientIdentity.Name, ClientIdentity.Version);
            }
            catch
            {
                ((IDisposable)channel).Dispose();
                throw;
            }
            return new ClientContext { Service = channel, Callback = callback };
        }

        /// <summary>
        /// Trust on first use: the server certificate thumbprint is saved on the first connection,
        /// and any later change is refused (possible impersonation of the server).
        /// </summary>
        private void VerifyPinnedCertificate(IScanService channel, string key)
        {
            var thumbprint = GetServerThumbprint(channel);
            if (thumbprint == null)
            {
                throw new InvalidOperationException("The scanning server did not present a certificate.");
            }
            var config = userConfigManager.Config;
            if (!config.PinnedServerThumbprints.TryGetValue(key, out var pinned))
            {
                config.PinnedServerThumbprints[key] = thumbprint;
                userConfigManager.Save();
                Log.Error("Trusted new scanning server {0} with certificate {1}", key, thumbprint);
                return;
            }
            if (!string.Equals(pinned, thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error("Scanning server certificate changed for {0}: expected {1}, got {2}", key, pinned, thumbprint);
                throw new InvalidOperationException("The scanning server certificate has changed. Connection refused.");
            }
        }

        private static string GetServerThumbprint(IScanService channel)
        {
            // The certificate the server presented on the TLS connection
            // NOT IMPLEMENTED YET: reading the presented certificate requires a validation callback
            // (see the note in the pinning section). Returning null makes the connection fail closed.
            return null;
        }
    }
}
