using System;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace NAPS2.ClientServer
{
    /// <summary>
    /// Only accepts connections from private (local network) or loopback addresses.
    /// </summary>
    public static class ServerAddressFilter
    {
        public static bool IsAllowed(IPAddress address)
        {
            if (address == null)
            {
                return false;
            }
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // Unique local (fc00::/7) and link-local (fe80::/10)
                var bytes = address.GetAddressBytes();
                return (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
            }
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        /// <summary>
        /// Returns the remote address of the current operation, or null if unknown.
        /// </summary>
        public static IPAddress CurrentRemoteAddress()
        {
            var context = OperationContext.Current;
            var prop = context?.IncomingMessageProperties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
            return prop != null && IPAddress.TryParse(prop.Address, out var ip) ? ip : null;
        }
    }
}
