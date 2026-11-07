using System;
using System.Globalization;
using System.Windows.Forms;
using NAPS2.Config;

namespace NAPS2.WinForms
{
    public partial class FVectorPdfDpi : FormBase
    {
        private readonly IUserConfigManager userConfigManager;

        public FVectorPdfDpi(IUserConfigManager userConfigManager)
        {
            this.userConfigManager = userConfigManager;
            InitializeComponent();
        }

        protected override void OnLoad(object sender, EventArgs e)
        {
            new LayoutManager(this)
                .Bind(btnRestoreDefaults, btnOK, btnCancel)
                    .BottomToForm()
                .Bind(lblTitle, tbDpi, lblInfo)
                    .WidthToForm()
                .Activate();

            tbDpi.Minimum = UserConfig.MIN_VECTOR_PDF_DPI;
            tbDpi.Maximum = UserConfig.MAX_VECTOR_PDF_DPI;
            UpdateValues(userConfigManager.Config.VectorPdfDpi);
        }

        private void UpdateValues(int dpi)
        {
            txtDpi.Text = dpi.ToString(CultureInfo.InvariantCulture);
            tbDpi.Value = Math.Max(tbDpi.Minimum, Math.Min(tbDpi.Maximum, dpi));
        }

        private void tbDpi_Scroll(object sender, EventArgs e)
        {
            txtDpi.Text = tbDpi.Value.ToString(CultureInfo.InvariantCulture);
        }

        private void txtDpi_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(txtDpi.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                && value >= tbDpi.Minimum && value <= tbDpi.Maximum)
            {
                tbDpi.Value = value;
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(txtDpi.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                || value < UserConfig.MIN_VECTOR_PDF_DPI || value > UserConfig.MAX_VECTOR_PDF_DPI)
            {
                MessageBox.Show(
                    $"Entrez une valeur entre {UserConfig.MIN_VECTOR_PDF_DPI} et {UserConfig.MAX_VECTOR_PDF_DPI} DPI.",
                    "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            userConfigManager.Config.VectorPdfDpi = value;
            userConfigManager.Save();
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnRestoreDefaults_Click(object sender, EventArgs e)
        {
            UpdateValues(UserConfig.DEFAULT_VECTOR_PDF_DPI);
        }
    }
}
