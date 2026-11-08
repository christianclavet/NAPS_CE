using System;
using System.Windows.Forms;
using NAPS2.Config;
using NAPS2.Scan;
using NAPS2.WinForms;

namespace NAPS2.ClientServer
{
    /// <summary>
    /// Lets the administrator of the scanning server choose the local scanner and its options.
    /// The result is saved as the server profile; connected clients only read it.
    /// </summary>
    public class ServerProfileDialog
    {
        private readonly IFormFactory formFactory;
        private readonly ServerProfileManager serverProfileManager;

        public ServerProfileDialog(IFormFactory formFactory, ServerProfileManager serverProfileManager)
        {
            this.formFactory = formFactory;
            this.serverProfileManager = serverProfileManager;
        }

        /// <summary>
        /// Opens the profile editor on the server profile. Returns true if the profile was saved.
        /// </summary>
        public bool Show(IWin32Window owner)
        {
            var form = formFactory.Create<FEditProfile>();
            form.IsServerConfig = true;
            form.ScanProfile = serverProfileManager.Profile;
            form.ShowDialog(owner);
            if (!form.Result)
            {
                return false;
            }
            var profile = form.ScanProfile;
            if (profile.Device == null || string.IsNullOrEmpty(profile.DriverName))
            {
                MessageBox.Show("Choisissez un numériseur avant d'enregistrer la configuration du serveur.",
                    "NAPS2 Serveur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            serverProfileManager.SetProfile(profile);
            return true;
        }
    }
}
