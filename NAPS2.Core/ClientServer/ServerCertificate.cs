using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using NAPS2.Logging;

namespace NAPS2.ClientServer
{
    /// <summary>
    /// The TLS certificate used by the NAPS2 scanning server. It is created once, on first run,
    /// in the current user's certificate store (via PowerShell, as the framework cannot create
    /// certificates on its own). Clients pin its thumbprint, so the certificate must stay in place.
    /// </summary>
    public static class ServerCertificate
    {
        public const string SUBJECT = "CN=NAPS2 Scanning Server";

        /// <summary>
        /// Returns the server certificate, creating it if needed.
        /// </summary>
        public static X509Certificate2 GetOrCreate()
        {
            var existing = Find();
            if (existing != null)
            {
                return existing;
            }
            Create();
            existing = Find();
            if (existing == null)
            {
                throw new InvalidOperationException("Could not create the NAPS2 server certificate.");
            }
            return existing;
        }

        private static X509Certificate2 Find()
        {
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates
                    .Cast<X509Certificate2>()
                    .Where(c => c.Subject == SUBJECT && c.NotAfter > DateTime.Now)
                    .OrderByDescending(c => c.NotAfter)
                    .FirstOrDefault();
            }
        }

        private static void Create()
        {
            // RSA 2048, server authentication only, valid 5 years, not exportable.
            const string script =
                "New-SelfSignedCertificate -Subject 'CN=NAPS2 Scanning Server' " +
                "-KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 " +
                "-KeyExportPolicy NonExportable -KeyUsage DigitalSignature,KeyEncipherment " +
                "-TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.1') " +
                "-CertStoreLocation 'Cert:\\CurrentUser\\My' -NotAfter (Get-Date).AddYears(5) | Out-Null";
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using (var proc = Process.Start(startInfo))
            {
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    Log.Error("Error creating server certificate: {0}", error);
                }
            }
        }
    }
}
