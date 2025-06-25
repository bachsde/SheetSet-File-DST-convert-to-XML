namespace SheetSet_File_DST_Converter
{
    partial class Form1
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
            this.btnGenerateDictionary = new System.Windows.Forms.Button();
            this.btnDstToXml = new System.Windows.Forms.Button();
            this.btnBrowseDict = new System.Windows.Forms.Button();
            this.btnBrowseXml = new System.Windows.Forms.Button();
            this.btnBrowseDst = new System.Windows.Forms.Button();
            this.txtDict = new System.Windows.Forms.TextBox();
            this.txtXml = new System.Windows.Forms.TextBox();
            this.txtDst = new System.Windows.Forms.TextBox();
            this.btnXmlToDst = new System.Windows.Forms.Button();
            this.btnExportExcel = new System.Windows.Forms.Button();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.btnXmlToTreeView = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // btnGenerateDictionary
            // 
            this.btnGenerateDictionary.Location = new System.Drawing.Point(279, 74);
            this.btnGenerateDictionary.Name = "btnGenerateDictionary";
            this.btnGenerateDictionary.Size = new System.Drawing.Size(121, 23);
            this.btnGenerateDictionary.TabIndex = 0;
            this.btnGenerateDictionary.Text = "Generate Dictionary";
            this.btnGenerateDictionary.UseVisualStyleBackColor = true;
            this.btnGenerateDictionary.Click += new System.EventHandler(this.btnGenerateDictionary_Click);
            // 
            // btnDstToXml
            // 
            this.btnDstToXml.Location = new System.Drawing.Point(406, 10);
            this.btnDstToXml.Name = "btnDstToXml";
            this.btnDstToXml.Size = new System.Drawing.Size(165, 23);
            this.btnDstToXml.TabIndex = 1;
            this.btnDstToXml.Text = "DST To XML";
            this.btnDstToXml.UseVisualStyleBackColor = true;
            this.btnDstToXml.Click += new System.EventHandler(this.btnDstToXml_Click);
            // 
            // btnBrowseDict
            // 
            this.btnBrowseDict.Location = new System.Drawing.Point(279, 106);
            this.btnBrowseDict.Name = "btnBrowseDict";
            this.btnBrowseDict.Size = new System.Drawing.Size(121, 23);
            this.btnBrowseDict.TabIndex = 13;
            this.btnBrowseDict.Text = "Browse Dictionary";
            this.btnBrowseDict.UseVisualStyleBackColor = true;
            this.btnBrowseDict.Click += new System.EventHandler(this.btnBrowseDict_Click_1);
            // 
            // btnBrowseXml
            // 
            this.btnBrowseXml.Location = new System.Drawing.Point(279, 42);
            this.btnBrowseXml.Name = "btnBrowseXml";
            this.btnBrowseXml.Size = new System.Drawing.Size(121, 23);
            this.btnBrowseXml.TabIndex = 12;
            this.btnBrowseXml.Text = "Browse .XML";
            this.btnBrowseXml.UseVisualStyleBackColor = true;
            this.btnBrowseXml.Click += new System.EventHandler(this.btnBrowseXml_Click_1);
            // 
            // btnBrowseDst
            // 
            this.btnBrowseDst.Location = new System.Drawing.Point(279, 10);
            this.btnBrowseDst.Name = "btnBrowseDst";
            this.btnBrowseDst.Size = new System.Drawing.Size(121, 23);
            this.btnBrowseDst.TabIndex = 11;
            this.btnBrowseDst.Text = "Browse .DST";
            this.btnBrowseDst.UseVisualStyleBackColor = true;
            this.btnBrowseDst.Click += new System.EventHandler(this.btnBrowseDst_Click_1);
            // 
            // txtDict
            // 
            this.txtDict.Location = new System.Drawing.Point(12, 108);
            this.txtDict.Name = "txtDict";
            this.txtDict.Size = new System.Drawing.Size(261, 20);
            this.txtDict.TabIndex = 10;
            // 
            // txtXml
            // 
            this.txtXml.Location = new System.Drawing.Point(12, 44);
            this.txtXml.Name = "txtXml";
            this.txtXml.Size = new System.Drawing.Size(261, 20);
            this.txtXml.TabIndex = 9;
            // 
            // txtDst
            // 
            this.txtDst.Location = new System.Drawing.Point(12, 12);
            this.txtDst.Name = "txtDst";
            this.txtDst.Size = new System.Drawing.Size(261, 20);
            this.txtDst.TabIndex = 8;
            // 
            // btnXmlToDst
            // 
            this.btnXmlToDst.Location = new System.Drawing.Point(406, 42);
            this.btnXmlToDst.Name = "btnXmlToDst";
            this.btnXmlToDst.Size = new System.Drawing.Size(165, 23);
            this.btnXmlToDst.TabIndex = 14;
            this.btnXmlToDst.Text = "XML To DST";
            this.btnXmlToDst.UseVisualStyleBackColor = true;
            this.btnXmlToDst.Click += new System.EventHandler(this.btnXmlToDst_Click);
            // 
            // btnExportExcel
            // 
            this.btnExportExcel.Location = new System.Drawing.Point(406, 106);
            this.btnExportExcel.Name = "btnExportExcel";
            this.btnExportExcel.Size = new System.Drawing.Size(165, 23);
            this.btnExportExcel.TabIndex = 15;
            this.btnExportExcel.Text = "Export XML To Excel";
            this.btnExportExcel.UseVisualStyleBackColor = true;
            this.btnExportExcel.Click += new System.EventHandler(this.btnExportExcel_Click);
            // 
            // treeView1
            // 
            this.treeView1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.treeView1.Location = new System.Drawing.Point(12, 137);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(559, 172);
            this.treeView1.TabIndex = 16;
            // 
            // btnXmlToTreeView
            // 
            this.btnXmlToTreeView.Location = new System.Drawing.Point(406, 74);
            this.btnXmlToTreeView.Name = "btnXmlToTreeView";
            this.btnXmlToTreeView.Size = new System.Drawing.Size(165, 23);
            this.btnXmlToTreeView.TabIndex = 17;
            this.btnXmlToTreeView.Text = "Read XML To TreeView";
            this.btnXmlToTreeView.UseVisualStyleBackColor = true;
            this.btnXmlToTreeView.Click += new System.EventHandler(this.btnXmlToTreeView_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(583, 321);
            this.Controls.Add(this.btnXmlToTreeView);
            this.Controls.Add(this.treeView1);
            this.Controls.Add(this.btnExportExcel);
            this.Controls.Add(this.btnXmlToDst);
            this.Controls.Add(this.btnBrowseDict);
            this.Controls.Add(this.btnBrowseXml);
            this.Controls.Add(this.btnBrowseDst);
            this.Controls.Add(this.txtDict);
            this.Controls.Add(this.txtXml);
            this.Controls.Add(this.txtDst);
            this.Controls.Add(this.btnDstToXml);
            this.Controls.Add(this.btnGenerateDictionary);
            this.MinimumSize = new System.Drawing.Size(599, 360);
            this.Name = "Form1";
            this.Text = "SheetSet File .DST Converter";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnGenerateDictionary;
        private System.Windows.Forms.Button btnDstToXml;
        private System.Windows.Forms.Button btnBrowseDict;
        private System.Windows.Forms.Button btnBrowseXml;
        private System.Windows.Forms.Button btnBrowseDst;
        private System.Windows.Forms.TextBox txtDict;
        private System.Windows.Forms.TextBox txtXml;
        private System.Windows.Forms.TextBox txtDst;
        private System.Windows.Forms.Button btnXmlToDst;
        private System.Windows.Forms.Button btnExportExcel;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.Button btnXmlToTreeView;
    }
}

