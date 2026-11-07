namespace NAPS2.WinForms
{
    partial class FVectorPdfDpi
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblInfo = new System.Windows.Forms.Label();
            this.txtDpi = new System.Windows.Forms.TextBox();
            this.tbDpi = new System.Windows.Forms.TrackBar();
            this.btnRestoreDefaults = new System.Windows.Forms.Button();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.tbDpi)).BeginInit();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Location = new System.Drawing.Point(12, 12);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(456, 15);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "Résolution de rastérisation des PDF vectoriels (DPI) :";
            // 
            // txtDpi
            // 
            this.txtDpi.Location = new System.Drawing.Point(12, 34);
            this.txtDpi.Name = "txtDpi";
            this.txtDpi.Size = new System.Drawing.Size(80, 23);
            this.txtDpi.TabIndex = 1;
            this.txtDpi.TextChanged += new System.EventHandler(this.txtDpi_TextChanged);
            // 
            // tbDpi
            // 
            this.tbDpi.Location = new System.Drawing.Point(12, 63);
            this.tbDpi.Maximum = 1200;
            this.tbDpi.Minimum = 72;
            this.tbDpi.Name = "tbDpi";
            this.tbDpi.Size = new System.Drawing.Size(456, 45);
            this.tbDpi.TabIndex = 2;
            this.tbDpi.TickFrequency = 100;
            this.tbDpi.Value = 300;
            this.tbDpi.Scroll += new System.EventHandler(this.tbDpi_Scroll);
            // 
            // lblInfo
            // 
            this.lblInfo.AutoSize = false;
            this.lblInfo.Location = new System.Drawing.Point(12, 112);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(456, 40);
            this.lblInfo.TabIndex = 3;
            this.lblInfo.Text = "Appliqué aux PDF importés. Une valeur plus élevée donne une image plus nette mais plus lourde et plus lente.";

            // 
            // btnRestoreDefaults
            // 
            this.btnRestoreDefaults.Location = new System.Drawing.Point(12, 160);
            this.btnRestoreDefaults.Name = "btnRestoreDefaults";
            this.btnRestoreDefaults.Size = new System.Drawing.Size(160, 27);
            this.btnRestoreDefaults.TabIndex = 4;
            this.btnRestoreDefaults.Text = "Restaurer par défaut";
            this.btnRestoreDefaults.UseVisualStyleBackColor = true;
            this.btnRestoreDefaults.Click += new System.EventHandler(this.btnRestoreDefaults_Click);
            // 
            // btnOK
            // 
            this.btnOK.Location = new System.Drawing.Point(312, 160);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 27);
            this.btnOK.TabIndex = 5;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(393, 160);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 27);
            this.btnCancel.TabIndex = 6;
            this.btnCancel.Text = "Annuler";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // FVectorPdfDpi
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(480, 200);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnRestoreDefaults);
            this.Controls.Add(this.lblInfo);
            this.Controls.Add(this.tbDpi);
            this.Controls.Add(this.txtDpi);
            this.Controls.Add(this.lblTitle);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FVectorPdfDpi";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Résolution des PDF vectoriels";
            ((System.ComponentModel.ISupportInitialize)(this.tbDpi)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblInfo;
        private System.Windows.Forms.TextBox txtDpi;
        private System.Windows.Forms.TrackBar tbDpi;
        private System.Windows.Forms.Button btnRestoreDefaults;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}
