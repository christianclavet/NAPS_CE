namespace NAPS2.WinForms
{
    partial class FBarcodeSettings
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
            this.cbAutoDetect = new System.Windows.Forms.CheckBox();
            this.cbDetectMultiple = new System.Windows.Forms.CheckBox();
            this.cbCreateDocument = new System.Windows.Forms.CheckBox();
            this.cbRemoveSeparator = new System.Windows.Forms.CheckBox();
            this.cbEnableFilter = new System.Windows.Forms.CheckBox();
            this.lblPattern = new System.Windows.Forms.Label();
            this.txtPattern = new System.Windows.Forms.TextBox();
            this.lblHelp = new System.Windows.Forms.Label();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // cbAutoDetect
            //
            this.cbAutoDetect.AutoSize = true;
            this.cbAutoDetect.Location = new System.Drawing.Point(15, 15);
            this.cbAutoDetect.Name = "cbAutoDetect";
            this.cbAutoDetect.Size = new System.Drawing.Size(330, 17);
            this.cbAutoDetect.TabIndex = 0;
            this.cbAutoDetect.Text = "Détecter les codes-barres après la numérisation (en arrière-plan)";
            this.cbAutoDetect.UseVisualStyleBackColor = true;
            //
            // cbDetectMultiple
            //
            this.cbDetectMultiple.AutoSize = true;
            this.cbDetectMultiple.Location = new System.Drawing.Point(15, 40);
            this.cbDetectMultiple.Name = "cbDetectMultiple";
            this.cbDetectMultiple.Size = new System.Drawing.Size(330, 17);
            this.cbDetectMultiple.TabIndex = 1;
            this.cbDetectMultiple.Text = "Détecter plusieurs codes-barres par page (séparés par ^)";
            this.cbDetectMultiple.UseVisualStyleBackColor = true;
            //
            // cbCreateDocument
            //
            this.cbCreateDocument.AutoSize = true;
            this.cbCreateDocument.Location = new System.Drawing.Point(15, 65);
            this.cbCreateDocument.Name = "cbCreateDocument";
            this.cbCreateDocument.Size = new System.Drawing.Size(330, 17);
            this.cbCreateDocument.TabIndex = 2;
            this.cbCreateDocument.Text = "Créer un nouveau document quand un code-barres est détecté";
            this.cbCreateDocument.UseVisualStyleBackColor = true;
            this.cbCreateDocument.CheckedChanged += new System.EventHandler(this.cbCreateDocument_CheckedChanged);
            //
            // cbRemoveSeparator
            //
            this.cbRemoveSeparator.AutoSize = true;
            this.cbRemoveSeparator.Location = new System.Drawing.Point(35, 88);
            this.cbRemoveSeparator.Name = "cbRemoveSeparator";
            this.cbRemoveSeparator.Size = new System.Drawing.Size(310, 17);
            this.cbRemoveSeparator.TabIndex = 3;
            this.cbRemoveSeparator.Text = "Supprimer la feuille séparatrice (recto et verso)";
            this.cbRemoveSeparator.UseVisualStyleBackColor = true;
            //
            // cbEnableFilter
            //
            this.cbEnableFilter.AutoSize = true;
            this.cbEnableFilter.Location = new System.Drawing.Point(15, 118);
            this.cbEnableFilter.Name = "cbEnableFilter";
            this.cbEnableFilter.Size = new System.Drawing.Size(260, 17);
            this.cbEnableFilter.TabIndex = 4;
            this.cbEnableFilter.Text = "Filtrer les codes-barres selon un format";
            this.cbEnableFilter.UseVisualStyleBackColor = true;
            this.cbEnableFilter.CheckedChanged += new System.EventHandler(this.cbEnableFilter_CheckedChanged);
            //
            // lblPattern
            //
            this.lblPattern.AutoSize = true;
            this.lblPattern.Location = new System.Drawing.Point(15, 148);
            this.lblPattern.Name = "lblPattern";
            this.lblPattern.Size = new System.Drawing.Size(120, 13);
            this.lblPattern.TabIndex = 5;
            this.lblPattern.Text = "Format accepté (regex) :";
            //
            // txtPattern
            //
            this.txtPattern.Location = new System.Drawing.Point(15, 165);
            this.txtPattern.Name = "txtPattern";
            this.txtPattern.Size = new System.Drawing.Size(360, 20);
            this.txtPattern.TabIndex = 6;
            //
            // lblHelp
            //
            this.lblHelp.AutoSize = false;
            this.lblHelp.Location = new System.Drawing.Point(15, 193);
            this.lblHelp.Name = "lblHelp";
            this.lblHelp.Size = new System.Drawing.Size(360, 40);
            this.lblHelp.TabIndex = 7;
            this.lblHelp.Text = "Exemple pour les codes SDOC : ^SDOC\\d{9}$\r\nLes codes qui ne correspondent pas sont ignorés (ils ne créent pas de document).";
            //
            // btnOK
            //
            this.btnOK.Location = new System.Drawing.Point(219, 243);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.TabIndex = 8;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            //
            // btnCancel
            //
            this.btnCancel.Location = new System.Drawing.Point(300, 243);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 9;
            this.btnCancel.Text = "Annuler";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // FBarcodeSettings
            //
            this.AcceptButton = this.btnOK;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(392, 279);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.lblHelp);
            this.Controls.Add(this.txtPattern);
            this.Controls.Add(this.lblPattern);
            this.Controls.Add(this.cbEnableFilter);
            this.Controls.Add(this.cbRemoveSeparator);
            this.Controls.Add(this.cbCreateDocument);
            this.Controls.Add(this.cbDetectMultiple);
            this.Controls.Add(this.cbAutoDetect);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FBarcodeSettings";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Options des codes-barres";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.CheckBox cbAutoDetect;
        private System.Windows.Forms.CheckBox cbDetectMultiple;
        private System.Windows.Forms.CheckBox cbCreateDocument;
        private System.Windows.Forms.CheckBox cbRemoveSeparator;
        private System.Windows.Forms.CheckBox cbEnableFilter;
        private System.Windows.Forms.Label lblPattern;
        private System.Windows.Forms.TextBox txtPattern;
        private System.Windows.Forms.Label lblHelp;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}
