using System;
using System.IdentityModel.Selectors;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using System.ServiceModel.Security;
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
            // The URI host is an IP address, but the certificate identifies the server by name,
            // so the expected DNS identity must be given explicitly.
            var endpoint = new EndpointAddress(uri, EndpointIdentity.CreateDnsIdentity(ServerCertificate.COMMON_NAME));
            var channelFactory = new DuplexChannelFactory<IScanService>(instanceContext, binding, endpoint);
            channelFactory.Credentials.ServiceCertificate.SslCertificateAuthentication = new X509ServiceCertificateAuthentication
            {
                CertificateValidationMode = X509CertificateValidationMode.Custom,
                CustomCertificateValidator = new PinningValidator(userConfigManager, key)
            };
            var channel = channelFactory.CreateChannel();
            try
            {
                channel.Hello(ClientIdentity.Name, ClientIdentity.Version);
            }
            catch (Exception ex)
            {
                // Log the real error first; closing a faulted channel can throw and hide it
                Log.ErrorException($"Could not connect to scanning server {key}", ex);
                try
                {
                    ((IDisposable)channel).Dispose();
                }
                catch (Exception)
                {
                    channelFactory.Abort();
                }
                throw;
            }
            return new ClientContext { Service = channel, Callback = callback };
        }

        /// <summary>
        /// Trust on first use: the server certificate thumbprint is saved on the first connection,
        /// and any later change is refused (possible impersonation of the server).
        /// </summary>
        private class PinningValidator : X509CertificateValidator
        {
            private readonly IUserConfigManager userConfigManager;
            private readonly string key;

            public PinningValidator(IUserConfigManager userConfigManager, string key)
            {
                this.userConfigManager = userConfigManager;
                this.key = key;
            }

            public override void Validate(X509Certificate2 certificate)
            {
                if (certificate == null)
                {
                    throw new SecurityTokenValidationException("The scanning server did not present a certificate.");
                }
                string thumbprint = certificate.Thumbprint;
                var config = userConfigManager.Config;
                var entry = config.PinnedServers.FirstOrDefault(x => x.Address == key);
                if (entry == null)
                {
                    config.PinnedServers.Add(new PinnedServer { Address = key, Thumbprint = thumbprint });
                    userConfigManager.Save();
                    Log.Error("Trusted new scanning server {0} with certificate {1}", key, thumbprint);
                    return;
                }
                if (!string.Equals(entry.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error("Scanning server certificate changed for {0}: expected {1}, got {2}", key, entry.Thumbprint, thumbprint);
                    throw new SecurityTokenValidationException("The scanning server certificate has changed. Connection refused.");
                }
            }
        }
    }
}
