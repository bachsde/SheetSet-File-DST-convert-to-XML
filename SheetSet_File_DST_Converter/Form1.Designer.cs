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
            btnGenerateDictionary = new System.Windows.Forms.Button();
            btnDstToXml = new System.Windows.Forms.Button();
            btnBrowseDict = new System.Windows.Forms.Button();
            btnBrowseXml = new System.Windows.Forms.Button();
            btnBrowseDst = new System.Windows.Forms.Button();
            txtDict = new System.Windows.Forms.TextBox();
            txtXml = new System.Windows.Forms.TextBox();
            txtDst = new System.Windows.Forms.TextBox();
            btnXmlToDst = new System.Windows.Forms.Button();
            btnExportExcel = new System.Windows.Forms.Button();
            treeView1 = new System.Windows.Forms.TreeView();
            btnXmlToTreeView = new System.Windows.Forms.Button();
            SuspendLayout();
            // 
            // btnGenerateDictionary
            // 
            btnGenerateDictionary.Location = new System.Drawing.Point(326, 85);
            btnGenerateDictionary.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnGenerateDictionary.Name = "btnGenerateDictionary";
            btnGenerateDictionary.Size = new System.Drawing.Size(141, 27);
            btnGenerateDictionary.TabIndex = 0;
            btnGenerateDictionary.Text = "Generate Dictionary";
            btnGenerateDictionary.UseVisualStyleBackColor = true;
            btnGenerateDictionary.Click += btnGenerateDictionary_Click;
            // 
            // btnDstToXml
            // 
            btnDstToXml.Location = new System.Drawing.Point(474, 12);
            btnDstToXml.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnDstToXml.Name = "btnDstToXml";
            btnDstToXml.Size = new System.Drawing.Size(192, 27);
            btnDstToXml.TabIndex = 1;
            btnDstToXml.Text = "DST To XML";
            btnDstToXml.UseVisualStyleBackColor = true;
            btnDstToXml.Click += btnDstToXml_Click;
            // 
            // btnBrowseDict
            // 
            btnBrowseDict.Location = new System.Drawing.Point(326, 122);
            btnBrowseDict.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnBrowseDict.Name = "btnBrowseDict";
            btnBrowseDict.Size = new System.Drawing.Size(141, 27);
            btnBrowseDict.TabIndex = 13;
            btnBrowseDict.Text = "Browse Dictionary";
            btnBrowseDict.UseVisualStyleBackColor = true;
            btnBrowseDict.Click += btnBrowseDict_Click_1;
            // 
            // btnBrowseXml
            // 
            btnBrowseXml.Location = new System.Drawing.Point(326, 48);
            btnBrowseXml.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnBrowseXml.Name = "btnBrowseXml";
            btnBrowseXml.Size = new System.Drawing.Size(141, 27);
            btnBrowseXml.TabIndex = 12;
            btnBrowseXml.Text = "Browse .XML";
            btnBrowseXml.UseVisualStyleBackColor = true;
            btnBrowseXml.Click += btnBrowseXml_Click_1;
            // 
            // btnBrowseDst
            // 
            btnBrowseDst.Location = new System.Drawing.Point(326, 12);
            btnBrowseDst.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnBrowseDst.Name = "btnBrowseDst";
            btnBrowseDst.Size = new System.Drawing.Size(141, 27);
            btnBrowseDst.TabIndex = 11;
            btnBrowseDst.Text = "Browse .DST";
            btnBrowseDst.UseVisualStyleBackColor = true;
            btnBrowseDst.Click += btnBrowseDst_Click_1;
            // 
            // txtDict
            // 
            txtDict.Location = new System.Drawing.Point(14, 125);
            txtDict.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            txtDict.Name = "txtDict";
            txtDict.Size = new System.Drawing.Size(304, 23);
            txtDict.TabIndex = 10;
            // 
            // txtXml
            // 
            txtXml.Location = new System.Drawing.Point(14, 51);
            txtXml.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            txtXml.Name = "txtXml";
            txtXml.Size = new System.Drawing.Size(304, 23);
            txtXml.TabIndex = 9;
            // 
            // txtDst
            // 
            txtDst.Location = new System.Drawing.Point(14, 14);
            txtDst.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            txtDst.Name = "txtDst";
            txtDst.Size = new System.Drawing.Size(304, 23);
            txtDst.TabIndex = 8;
            // 
            // btnXmlToDst
            // 
            btnXmlToDst.Location = new System.Drawing.Point(474, 48);
            btnXmlToDst.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnXmlToDst.Name = "btnXmlToDst";
            btnXmlToDst.Size = new System.Drawing.Size(192, 27);
            btnXmlToDst.TabIndex = 14;
            btnXmlToDst.Text = "XML To DST";
            btnXmlToDst.UseVisualStyleBackColor = true;
            btnXmlToDst.Click += btnXmlToDst_Click;
            // 
            // btnExportExcel
            // 
            btnExportExcel.Location = new System.Drawing.Point(474, 122);
            btnExportExcel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnExportExcel.Name = "btnExportExcel";
            btnExportExcel.Size = new System.Drawing.Size(192, 27);
            btnExportExcel.TabIndex = 15;
            btnExportExcel.Text = "Export XML To Excel";
            btnExportExcel.UseVisualStyleBackColor = true;
            btnExportExcel.Click += btnExportExcel_Click;
            // 
            // treeView1
            // 
            treeView1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            treeView1.Location = new System.Drawing.Point(14, 158);
            treeView1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            treeView1.Name = "treeView1";
            treeView1.Size = new System.Drawing.Size(652, 198);
            treeView1.TabIndex = 16;
            // 
            // btnXmlToTreeView
            // 
            btnXmlToTreeView.Location = new System.Drawing.Point(474, 85);
            btnXmlToTreeView.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnXmlToTreeView.Name = "btnXmlToTreeView";
            btnXmlToTreeView.Size = new System.Drawing.Size(192, 27);
            btnXmlToTreeView.TabIndex = 17;
            btnXmlToTreeView.Text = "Read XML To TreeView";
            btnXmlToTreeView.UseVisualStyleBackColor = true;
            btnXmlToTreeView.Click += btnXmlToTreeView_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(680, 370);
            Controls.Add(btnXmlToTreeView);
            Controls.Add(treeView1);
            Controls.Add(btnExportExcel);
            Controls.Add(btnXmlToDst);
            Controls.Add(btnBrowseDict);
            Controls.Add(btnBrowseXml);
            Controls.Add(btnBrowseDst);
            Controls.Add(txtDict);
            Controls.Add(txtXml);
            Controls.Add(txtDst);
            Controls.Add(btnDstToXml);
            Controls.Add(btnGenerateDictionary);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MinimumSize = new System.Drawing.Size(696, 409);
            Name = "Form1";
            Text = "SheetSet File .DST Converter - Dev by Ba'ch Nguyen";
            ResumeLayout(false);
            PerformLayout();

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

