using System;
using System.Reflection;

namespace NAPS2.ClientServer
{
    /// <summary>
    /// Identifies this application to a NAPS2 server. Sent by clients on connection.
    /// </summary>
    public static class ClientIdentity
    {
        public const string EXPECTED_PRODUCT = "NAPS2 CriCri Edition";

        public static string Name => EXPECTED_PRODUCT;

        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }
}
