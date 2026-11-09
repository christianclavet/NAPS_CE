using System;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NAPS2.Config;

namespace NAPS2.WinForms
{
    public partial class FBarcodeSettings : FormBase
    {
        private readonly IUserConfigManager userConfigManager;

        public FBarcodeSettings(IUserConfigManager userConfigManager)
        {
            this.userConfigManager = userConfigManager;
            InitializeComponent();
        }

        protected override void OnLoad(object sender, EventArgs eventArgs)
        {
            var config = userConfigManager.Config;
            cbAutoDetect.Checked = config.BarcodeAutoDetectAfterScan;
            cbEnableFilter.Checked = config.BarcodeFilterEnabled;
            txtPattern.Text = config.BarcodeFilterPattern ?? "";
            UpdateControls();
        }

        private void UpdateControls()
        {
            txtPattern.Enabled = lblPattern.Enabled = cbEnableFilter.Checked;
        }

        private void cbEnableFilter_CheckedChanged(object sender, EventArgs e)
        {
            UpdateControls();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            string pattern = txtPattern.Text.Trim();

            if (cbEnableFilter.Checked)
            {
                // Un motif invalide ne doit pas être enregistré : il ferait échouer tous les scans
                if (string.IsNullOrEmpty(pattern) || !IsValidPattern(pattern))
                {
                    MessageBox.Show("Expression régulière invalide.", "Filtre des codes-barres",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtPattern.Focus();
                    return;
                }
            }

            var config = userConfigManager.Config;
            config.BarcodeAutoDetectAfterScan = cbAutoDetect.Checked;
            config.BarcodeFilterEnabled = cbEnableFilter.Checked;
            config.BarcodeFilterPattern = pattern;
            userConfigManager.Save();

            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool IsValidPattern(string pattern)
        {
            try
            {
                Regex.IsMatch("", pattern);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
