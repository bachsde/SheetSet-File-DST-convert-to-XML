using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using SheetSet_File_DST_Converter.Helper;

namespace SheetSet_File_DST_Converter
{
    public partial class Form1 : Form
    {
        private const string XmlFilter = "XML files (*.xml)|*.xml|All files (*.*)|*.*";
        private const string DstFilter = "DST files (*.dst)|*.dst|All files (*.*)|*.*";
        private const string JsonFilter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        private const int TreeExpandDepth = 3;

        private Font boldFont; // dùng chung cho mọi node, dispose khi đóng form

        public Form1()
        {
            InitializeComponent();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (boldFont != null)
            {
                boldFont.Dispose();
                boldFont = null;
            }
            base.OnFormClosed(e);
        }

        #region Convert

        private void btnDstToXml_Click(object sender, EventArgs e)
        {
            try
            {
                string dstPath = txtDst.Text.Trim();
                if (!RequireFile(dstPath, "Vui lòng chọn file .dst hợp lệ.")) return;

                var converter = CreateConverter();
                byte[] xml = converter.DstToXml(File.ReadAllBytes(dstPath));

                // Kết quả không phải XML hợp lệ → nhiều khả năng dictionary sai
                string error;
                if (!DstXmlConverter.IsWellFormedXml(xml, out error))
                {
                    var answer = MessageBox.Show(
                        "Kết quả giải mã không phải XML hợp lệ (" + error + ").\n" +
                        "Có thể dictionary chưa đúng.\n\nVẫn lưu file?",
                        "Cảnh báo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) return;
                }

                string xmlPath = AskSavePath("Chọn nơi lưu file XML", XmlFilter,
                    Path.GetFileNameWithoutExtension(dstPath) + "_converted.xml",
                    Path.GetDirectoryName(dstPath));
                if (xmlPath == null) return;

                File.WriteAllBytes(xmlPath, xml);
                txtXml.Text = xmlPath;

                var result = MessageBox.Show(
                    "DST → XML đã hoàn tất:\n" + xmlPath + "\n\nBạn có muốn load XML này vào TreeView không?",
                    "Hoàn tất chuyển đổi", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                    LoadXmlFullToTreeView(xmlPath);
            }
            catch (IOException ex) when (IsFileLocked(ex))
            {
                ShowError("Lỗi", "File đang bị chương trình khác sử dụng.\n\n" + ex.Message);
            }
            catch (Exception ex)
            {
                ShowError("Lỗi DST → XML", ex.Message);
            }
        }

        private void btnXmlToDst_Click(object sender, EventArgs e)
        {
            try
            {
                string xmlPath = txtXml.Text.Trim();
                if (!RequireFile(xmlPath, "Vui lòng chọn file .xml hợp lệ.")) return;

                var converter = CreateConverter();

                // Kiểm tra UTF-16 / BOM / XML hợp lệ, rồi mã hoá TRƯỚC khi hỏi nơi lưu
                bool bomRemoved;
                byte[] xml = converter.PrepareXmlForEncoding(File.ReadAllBytes(xmlPath), out bomRemoved);
                byte[] dst = converter.XmlToDst(xml);

                string dstPath = AskSavePath("Chọn nơi lưu file DST", DstFilter,
                    Path.GetFileNameWithoutExtension(xmlPath) + "_converted.dst",
                    Path.GetDirectoryName(xmlPath));
                if (dstPath == null) return;

                // Kiểm tra an toàn: file bị khoá / AutoCAD đang chạy
                if (File.Exists(dstPath))
                {
                    if (!DstFileGuard.ConfirmNotLocked(this, dstPath)) return;
                    if (!DstFileGuard.ConfirmAutoCadClosed(this)) return;
                }

                // Backup nếu ghi đè file có sẵn
                string backupPath = null;
                if (File.Exists(dstPath))
                {
                    backupPath = dstPath + ".bak";
                    File.Copy(dstPath, backupPath, true);
                }

                File.WriteAllBytes(dstPath, dst);
                txtDst.Text = dstPath;

                string msg = "XML → DST đã hoàn tất:\n" + dstPath;
                if (backupPath != null) msg += "\n\nFile cũ đã được backup:\n" + backupPath;
                if (bomRemoved) msg += "\n\nLưu ý: đã bỏ BOM UTF-8 ở đầu file XML.";
                ShowInfo(msg);
            }
            catch (IOException ex) when (IsFileLocked(ex))
            {
                ShowError("Không ghi được file DST",
                    "File đang bị khoá — có thể AutoCAD đang mở sheet set này.\n" +
                    "Hãy đóng sheet set trong Sheet Set Manager rồi thử lại.\n\n" + ex.Message);
            }
            catch (Exception ex)
            {
                ShowError("Lỗi XML → DST", ex.Message);
            }
        }

        private void btnGenerateDictionary_Click(object sender, EventArgs e)
        {
            try
            {
                string dstPath = txtDst.Text.Trim();
                string xmlPath = txtXml.Text.Trim();

                if (!RequireFile(dstPath, "Vui lòng chọn file .dst mẫu hợp lệ.")) return;
                if (!RequireFile(xmlPath,
                        "Vui lòng chọn file .xml mẫu hợp lệ\n(bản giải mã nguyên gốc của chính file .dst đó).")) return;

                // Gộp: built-in + dictionary hiện tại (nếu có) + cặp file mẫu
                var converter = CreateConverter();
                int added = converter.Learn(File.ReadAllBytes(xmlPath), File.ReadAllBytes(dstPath));

                string defaultDir = Path.GetDirectoryName(dstPath);
                string outJson = AskSavePath("Lưu file dictionary", JsonFilter, "dictionary.json", defaultDir);
                if (outJson == null) return;

                converter.SaveDictionary(outJson);
                txtDict.Text = outJson;

                ShowInfo($"Đã lưu dictionary:\n{outJson}\n\n" +
                         $"Học thêm {added} byte mới. Tổng cộng đã biết {converter.KnownByteCount}/256 byte.");
            }
            catch (Exception ex)
            {
                ShowError("Lỗi tạo dictionary", ex.Message);
            }
        }

        #endregion

        #region Excel

        private void btnExportExcel_Click(object sender, EventArgs e)
        {
            try
            {
                var source = LoadSource(CreateConverter());
                if (source == null) return;

                var doc = source.ToXmlDocument();

                string suggestedName = Path.GetFileNameWithoutExtension(source.FilePath) + "_" +
                                       DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx";

                string excelPath = AskSavePath("Xuất Excel", "Excel Files (*.xlsx)|*.xlsx",
                    suggestedName, Path.GetDirectoryName(source.FilePath));
                if (excelPath == null) return;

                int count = SheetSetExcelExporter.Export(doc, excelPath);
                ShowInfo($"Đã xuất {count} sheet sang Excel:\n{excelPath}");
            }
            catch (IOException ex) when (IsFileLocked(ex))
            {
                ShowError("Lỗi xuất Excel", "File Excel đang được mở. Hãy đóng file rồi thử lại.\n\n" + ex.Message);
            }
            catch (Exception ex)
            {
                ShowError("Lỗi xuất Excel", ex.Message);
            }
        }

        #endregion

        #region Sheet Set Viewer

        /// <summary>
        /// Mở cửa sổ xem / sửa giống Sheet Set Manager.
        /// Đọc thẳng file .dst (giải mã trong bộ nhớ); ô DST trống thì dùng file XML.
        /// </summary>
        private void btnSheetSetViewer_Click(object sender, EventArgs e)
        {
            try
            {
                var converter = CreateConverter();
                var source = LoadSource(converter);
                if (source == null) return;

                Cursor = Cursors.WaitCursor;
                try
                {
                    // Đường dẫn tương đối của DWG tính từ thư mục chứa file nguồn
                    var document = SheetSetDocument.Load(source.Xml, Path.GetDirectoryName(source.FilePath));

                    // Mở từ DST → Build DST mặc định ghi đè đúng file đó (có backup).
                    // Mở từ XML → không đoán file DST đích để tránh ghi nhầm sheet set khác.
                    new SheetSetViewerForm(document, converter, source.FilePath,
                        source.IsDst ? source.FilePath : null, source.DstHash).Show(this);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
            catch (Exception ex)
            {
                ShowError("Lỗi mở Sheet Set Viewer", ex.Message);
            }
        }

        #endregion

        #region TreeView

        private void btnXmlToTreeView_Click(object sender, EventArgs e)
        {
            try
            {
                var source = LoadSource(CreateConverter());
                if (source == null) return;

                LoadXmlFullToTreeView(source.ToXmlDocument());
            }
            catch (Exception ex)
            {
                ShowError("Lỗi đọc Sheet Set", ex.Message);
            }
        }

        private void LoadXmlFullToTreeView(string xmlPath)
        {
            var doc = new XmlDocument { XmlResolver = null };
            doc.Load(xmlPath);
            LoadXmlFullToTreeView(doc);
        }

        private void LoadXmlFullToTreeView(XmlDocument doc)
        {
            if (boldFont == null)
                boldFont = new Font(treeView1.Font, FontStyle.Bold);

            Cursor = Cursors.WaitCursor;
            treeView1.BeginUpdate();
            try
            {
                treeView1.Nodes.Clear();

                var rootNode = new TreeNode(doc.DocumentElement.Name) { NodeFont = boldFont };
                treeView1.Nodes.Add(rootNode);
                AddXmlNodeRecursive(doc.DocumentElement, rootNode);

                // ExpandAll() rất chậm với sheet set lớn → chỉ mở vài cấp đầu
                ExpandToDepth(rootNode, TreeExpandDepth);
                rootNode.EnsureVisible();
            }
            finally
            {
                treeView1.EndUpdate();
                Cursor = Cursors.Default;
            }
        }

        private void AddXmlNodeRecursive(XmlNode xmlNode, TreeNode treeNode)
        {
            // Thuộc tính (xanh lá)
            if (xmlNode.Attributes != null)
            {
                foreach (XmlAttribute attr in xmlNode.Attributes)
                {
                    treeNode.Nodes.Add(new TreeNode($"@{attr.Name} = \"{attr.Value}\"")
                    {
                        ForeColor = Color.ForestGreen
                    });
                }
            }

            foreach (XmlNode child in xmlNode.ChildNodes)
            {
                switch (child.NodeType)
                {
                    case XmlNodeType.Element:
                        // Gán NodeFont trước khi Add để tránh lỗi WinForms cắt chữ node in đậm
                        var childNode = new TreeNode(GetElementLabel(child)) { NodeFont = boldFont };
                        treeNode.Nodes.Add(childNode);
                        AddXmlNodeRecursive(child, childNode);
                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        if (!string.IsNullOrWhiteSpace(child.Value))
                        {
                            treeNode.Nodes.Add(new TreeNode($"#text = \"{child.Value.Trim()}\"")
                            {
                                ForeColor = Color.RoyalBlue
                            });
                        }
                        break;
                }
            }
        }

        /// <summary>Hiện thêm propname cho dễ tìm, ví dụ: AcSmProp [Title]</summary>
        private static string GetElementLabel(XmlNode element)
        {
            string propName = element.Attributes?["propname"]?.Value;
            return string.IsNullOrEmpty(propName) ? element.Name : $"{element.Name} [{propName}]";
        }

        private static void ExpandToDepth(TreeNode node, int depth)
        {
            if (depth <= 0) return;
            node.Expand();
            foreach (TreeNode child in node.Nodes)
                ExpandToDepth(child, depth - 1);
        }

        #endregion

        #region Browse

        private void btnBrowseDst_Click(object sender, EventArgs e)
        {
            string path = AskOpenPath(DstFilter, txtDst.Text);
            if (path != null) txtDst.Text = path;
        }

        private void btnBrowseXml_Click(object sender, EventArgs e)
        {
            string path = AskOpenPath(XmlFilter, txtXml.Text);
            if (path != null) txtXml.Text = path;
        }

        private void btnBrowseDict_Click(object sender, EventArgs e)
        {
            string path = AskOpenPath(JsonFilter, txtDict.Text);
            if (path != null) txtDict.Text = path;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Nguồn dữ liệu sheet set cho Viewer / Excel / TreeView:
        /// ưu tiên file .dst (giải mã trong bộ nhớ, không cần tạo XML), ô DST trống thì dùng file .xml.
        /// Trả null (đã báo lỗi) nếu không có file hợp lệ.
        /// </summary>
        private SheetSetSource LoadSource(DstXmlConverter converter)
        {
            string dstPath = txtDst.Text.Trim();
            string xmlPath = txtXml.Text.Trim();

            if (!string.IsNullOrWhiteSpace(dstPath))
            {
                if (!File.Exists(dstPath))
                {
                    MessageBox.Show("Không tìm thấy file .dst:\n" + dstPath, "Thiếu file",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                byte[] dstBytes = File.ReadAllBytes(dstPath);
                byte[] xml = converter.DstToXml(dstBytes);
                return new SheetSetSource(xml, dstPath, true, DstFileGuard.ComputeHash(dstBytes));
            }

            if (!string.IsNullOrWhiteSpace(xmlPath) && File.Exists(xmlPath))
                return new SheetSetSource(File.ReadAllBytes(xmlPath), xmlPath, false, null);

            MessageBox.Show("Vui lòng chọn file .dst (hoặc file .xml).", "Thiếu file",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        private sealed class SheetSetSource
        {
            public SheetSetSource(byte[] xml, string path, bool isDst, string dstHash)
            {
                Xml = xml;
                FilePath = path;
                IsDst = isDst;
                DstHash = dstHash;
            }

            /// <summary>Mã băm file .dst lúc đọc (null nếu nguồn là XML).</summary>
            public string DstHash { get; }

            public byte[] Xml { get; }
            public string FilePath { get; }
            public bool IsDst { get; }

            public XmlDocument ToXmlDocument()
            {
                var doc = new XmlDocument { XmlResolver = null };
                using (var ms = new MemoryStream(Xml))
                    doc.Load(ms);
                return doc;
            }
        }

        /// <summary>Built-in + file dictionary trong ô txtDict (nếu có).</summary>
        private DstXmlConverter CreateConverter()
        {
            return DstXmlConverter.Create(txtDict.Text.Trim());
        }

        private static bool RequireFile(string path, string message)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return true;
            MessageBox.Show(message, "Thiếu file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private static string AskSavePath(string title, string filter, string fileName, string initialDir)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = title;
                sfd.Filter = filter;
                sfd.FileName = fileName;
                if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                    sfd.InitialDirectory = initialDir;

                return sfd.ShowDialog() == DialogResult.OK ? sfd.FileName : null;
            }
        }

        private static string AskOpenPath(string filter, string currentPath)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = filter;
                try
                {
                    string dir = string.IsNullOrWhiteSpace(currentPath) ? null : Path.GetDirectoryName(currentPath.Trim());
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        ofd.InitialDirectory = dir;
                }
                catch (ArgumentException) { /* đường dẫn trong ô không hợp lệ → bỏ qua */ }

                return ofd.ShowDialog() == DialogResult.OK ? ofd.FileName : null;
            }
        }

        /// <summary>ERROR_SHARING_VIOLATION (32) hoặc ERROR_LOCK_VIOLATION (33).</summary>
        private static bool IsFileLocked(IOException ex)
        {
            int code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33;
        }

        private static void ShowInfo(string message)
        {
            MessageBox.Show(message, "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void ShowError(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        #endregion
    }
}
