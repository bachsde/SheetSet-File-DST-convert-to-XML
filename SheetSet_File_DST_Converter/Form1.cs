using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using SheetSet_File_DST_Converter.Helper;

namespace SheetSet_File_DST_Converter
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        private string dstFilePath = "";
        private void btnGenerateDictionary_Click(object sender, EventArgs e)
        {
            string xmlPath = txtXml.Text.Trim();
            string dstPath = txtDst.Text.Trim();
            string outJson = Path.Combine(Path.GetDirectoryName(dstPath), "dictionary.json");

            try
            {
                var converter = new DstXmlConverter();
                converter.RestoreDictionary(xmlPath, dstPath, outJson);
                MessageBox.Show("Đã xuất dictionary.json tại:\n" + outJson);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi tạo dictionary: " + ex.Message);
            }
        }

        private void btnDstToXml_Click(object sender, EventArgs e)
        {

            try
            {
                string dstPath = txtDst.Text.Trim();
                string dictPath = txtDict.Text.Trim();

                // Kiểm tra đầu vào
                if (string.IsNullOrWhiteSpace(dstPath) || !File.Exists(dstPath))
                {
                    MessageBox.Show("Vui lòng chọn file .dst hợp lệ.");
                    return;
                }



                // Gợi ý tên file XML bằng SaveFileDialog
                string xmlPath;
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Title = "Chọn nơi lưu file XML";
                    sfd.Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*";
                    sfd.FileName = Path.GetFileNameWithoutExtension(dstPath) + "_converted.xml";
                    sfd.InitialDirectory = Path.GetDirectoryName(dstPath);

                    if (sfd.ShowDialog() != DialogResult.OK)
                        return;

                    xmlPath = sfd.FileName;
                    txtXml.Text = xmlPath; // Cập nhật lại textbox (nếu dùng)
                }

                var converter = new DstXmlConverter();
                //converter.LoadDictionary(dictPath, "dst_to_xml");


                if (!string.IsNullOrEmpty(dictPath) && File.Exists(dictPath))
                {
                    converter.LoadDictionary(dictPath, "dst_to_xml");
                }
                else
                {
                    converter.LoadBuiltIn("dst_to_xml");
                }

                byte[] input = File.ReadAllBytes(dstPath);
                byte[] output = converter.Convert(input);
                File.WriteAllBytes(xmlPath, output);



                var result = MessageBox.Show(
                    "DST → XML đã hoàn tất:\n" + xmlPath + "\n\nBạn có muốn load XML này vào TreeView không?",
                    "Hoàn tất chuyển đổi",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    LoadXmlFullToTreeView(xmlPath);
                }


            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi: " + ex.Message);
            }


        }


        private void btnBrowseDst_Click_1(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "DST files (*.dst)|*.dst";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    dstFilePath = ofd.FileName;
                    txtDst.Text = dstFilePath;
                }
            }
        }

        private void btnBrowseXml_Click_1(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtXml.Text = dlg.FileName;
            }
        }

        private void btnBrowseDict_Click_1(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtDict.Text = dlg.FileName;
            }
        }

        private void btnExportExcel_Click(object sender, EventArgs e)
        {
            
            try
            {
                string xmlPath = txtXml.Text.Trim();
                if (!File.Exists(xmlPath))
                {
                    MessageBox.Show("Vui lòng chọn file XML hợp lệ.");
                    return;
                }

                var doc = new XmlDocument();
                doc.Load(xmlPath);

                string sheetSetName = Path.GetFileNameWithoutExtension(xmlPath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string suggestedName = $"{sheetSetName}_{timestamp}.xlsx";

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Excel Files (*.xlsx)|*.xlsx";
                    sfd.FileName = suggestedName;

                    if (sfd.ShowDialog() != DialogResult.OK)
                        return;

                    string excelPath = sfd.FileName;
                    var rows = new List<Dictionary<string, string>>();

                    foreach (XmlNode sheet in doc.SelectNodes("//AcSmSheet"))
                    {
                        var row = new Dictionary<string, string>();

                        row["Sheet ID"] = sheet.Attributes["ID"]?.InnerText ?? "";

                        // Subset
                        if (sheet.ParentNode?.Name == "AcSmSubset")
                        {
                            var subset = sheet.ParentNode;
                            row["Subset Name"] = subset.SelectSingleNode("AcSmProp[@propname='Name']")?.InnerText?.Trim() ?? "";
                            row["Subset Description"] = subset.SelectSingleNode("AcSmProp[@propname='Desc']")?.InnerText?.Trim() ?? "";
                        }

                        // Sheet props
                        foreach (var prop in new[] {
                    "Number", "Title", "Desc", "IncludeForPublish", "RevisionNumber",
                    "RevisionDate", "Purpose", "Category", "Folder"
                })
                        {
                            var node = sheet.SelectSingleNode($"AcSmProp[@propname='{prop}']");
                            row[PropToColumn(prop)] = node?.InnerText?.Trim() ?? "";
                        }

                        // Layout
                        var layout = sheet.SelectSingleNode("AcSmAcDbLayoutReference");
                        if (layout != null)
                        {
                            row["Layout"] = GetLayoutProp(layout, "Name");
                            row["Relative Path"] = GetLayoutProp(layout, "Relative_FileName");
                            row["Environment Path"] = GetLayoutProp(layout, "Environ_FileName");
                            row["UNC Path"] = GetLayoutProp(layout, "Unc_FileName");
                            row["Full Path"] = GetLayoutProp(layout, "FileName");
                        }

                        // Custom props: trong Bag
                        foreach (XmlNode custom in sheet.SelectNodes("AcSmCustomPropertyBag/AcSmCustomPropertyValue"))
                        {
                            string propName = custom.Attributes["propname"]?.InnerText?.Trim();
                            string value = custom.SelectSingleNode("AcSmProp[@propname='Value']")?.InnerText?.Trim();
                            if (!string.IsNullOrEmpty(propName)) row[propName] = value ?? "";
                        }

                        // Custom props: trực tiếp
                        foreach (XmlNode custom in sheet.SelectNodes("AcSmCustomPropertyValue"))
                        {
                            string propName = custom.Attributes["propname"]?.InnerText?.Trim();
                            string value = custom.SelectSingleNode("AcSmProp[@propname='Value']")?.InnerText?.Trim();
                            if (!string.IsNullOrEmpty(propName)) row[propName] = value ?? "";
                        }

                        // File Name (sau layout/custom)
                        var layoutRef = sheet.SelectSingleNode("AcSmAcDbLayoutReference");
                        if (layoutRef != null)
                        {
                            row["File Name"] = GetLayoutProp(layoutRef, "FileName");
                        }

                        rows.Add(row);
                    }

                    // Cột cố định theo thứ tự 
                    var fixedHeaders = new[]
                    {
                "Sheet ID", "Subset Name", "Subset Description",
                "Sheet number", "Sheet title", "Description", "Include for publish",
                "Revision number", "Revision date", "Purpose", "Category", "Folder",
                "Layout", "Relative Path", "Environment Path", "UNC Path", "Full Path",
                "File Name"
            };

                    var dynamicHeaders = rows
                        .SelectMany(r => r.Keys)
                        .Where(k => !fixedHeaders.Contains(k))
                        .Distinct()
                        .OrderBy(k => k)
                        .ToList();

                    var headers = fixedHeaders.Concat(dynamicHeaders).ToList();

                    using (var wb = new XLWorkbook())
                    {
                        var ws = wb.Worksheets.Add("Sheets");

                        for (int i = 0; i < headers.Count; i++)
                        {
                            var cell = ws.Cell(1, i + 1);
                            cell.Value = headers[i];
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
                        }

                        for (int r = 0; r < rows.Count; r++)
                        {
                            for (int c = 0; c < headers.Count; c++)
                            {
                                rows[r].TryGetValue(headers[c], out string val);
                                ws.Cell(r + 2, c + 1).Value = val;
                            }
                        }

                        foreach (var col in ws.ColumnsUsed())
                        {
                            col.AdjustToContents();
                            if (col.Width > 100) col.Width = 100;
                        }

                        wb.SaveAs(excelPath);
                    }

                    MessageBox.Show("Đã xuất dữ liệu sang Excel:\n" + excelPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi xuất Excel: " + ex.Message);
            }


        }


        private static string GetLayoutProp(XmlNode layoutNode, string propname)
        {
            return layoutNode.SelectSingleNode($"AcSmProp[@propname='{propname}']")?.InnerText?.Trim() ?? "";
        }

        private static string PropToColumn(string prop)
        {
            if (prop == "Number") return "Sheet number";
            if (prop == "Title") return "Sheet title";
            if (prop == "Desc") return "Description";
            if (prop == "IncludeForPublish") return "Include for publish";
            if (prop == "RevisionNumber") return "Revision number";
            if (prop == "RevisionDate") return "Revision date";
            if (prop == "Purpose") return "Purpose";
            if (prop == "Category") return "Category";
            if (prop == "Folder") return "Folder";
            return prop;
        }



        private void LoadXmlFullToTreeView(string xmlPath)
        {
            treeView1.Nodes.Clear();
            var doc = new XmlDocument();
            doc.Load(xmlPath);

            TreeNode rootNode = treeView1.Nodes.Add(doc.DocumentElement.Name);
            AddXmlNodeRecursive(doc.DocumentElement, rootNode);

            treeView1.ExpandAll();
        }



        private void AddXmlNodeRecursive(XmlNode xmlNode, TreeNode treeNode)
        {
            // Thuộc tính (màu xanh lá)
            if (xmlNode.Attributes != null)
            {
                foreach (XmlAttribute attr in xmlNode.Attributes)
                {
                    var attrNode = treeNode.Nodes.Add($"@{attr.Name} = \"{attr.Value}\"");
                    attrNode.ForeColor = Color.ForestGreen;
                }
            }

            // Nội dung văn bản thuần
            if (!string.IsNullOrWhiteSpace(xmlNode.InnerText) && xmlNode.ChildNodes.Count == 1 && xmlNode.FirstChild is XmlText)
            {
                var textNode = treeNode.Nodes.Add($"#text = \"{xmlNode.InnerText.Trim()}\"");
                textNode.ForeColor = Color.RoyalBlue;
                return;
            }

            // Node con là thẻ XML
            foreach (XmlNode child in xmlNode.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                {
                    var childNode = treeNode.Nodes.Add(child.Name);
                    childNode.NodeFont = new Font(treeView1.Font, FontStyle.Bold);
                    AddXmlNodeRecursive(child, childNode);
                }
            }
        }

        private void btnXmlToDst_Click(object sender, EventArgs e)
        {
            try
            {
                string xmlPath = txtXml.Text.Trim();
                string dictPath = txtDict.Text.Trim();

                // Kiểm tra đầu vào
                if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
                {
                    MessageBox.Show("Vui lòng chọn file .xml hợp lệ.");
                    return;
                }

                // Gợi ý tên file .dst bằng SaveFileDialog
                string dstPath;
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Title = "Chọn nơi lưu file DST";
                    sfd.Filter = "DST files (*.dst)|*.dst|All files (*.*)|*.*";
                    sfd.FileName = Path.GetFileNameWithoutExtension(xmlPath) + "_converted.dst";
                    sfd.InitialDirectory = Path.GetDirectoryName(xmlPath);

                    if (sfd.ShowDialog() != DialogResult.OK)
                        return;

                    dstPath = sfd.FileName;
                    txtDst.Text = dstPath; // Cập nhật lại textbox nếu có
                }

                var converter = new DstXmlConverter();

                // Load dictionary (từ textbox hoặc embedded)
                if (!string.IsNullOrEmpty(dictPath) && File.Exists(dictPath))
                {
                    converter.LoadDictionary(dictPath, "xml_to_dst");
                }
                else
                {
                    converter.LoadBuiltIn("xml_to_dst");
                }

                byte[] input = File.ReadAllBytes(xmlPath);
                byte[] output = converter.Convert(input);
                File.WriteAllBytes(dstPath, output);

                MessageBox.Show("XML → DST đã hoàn tất:\n" + dstPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi: " + ex.Message);
            }
        }

        private void btnXmlToTreeView_Click(object sender, EventArgs e)
        {
            string xmlPath = txtXml.Text.Trim();

            if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
            {
                MessageBox.Show("Vui lòng chọn file XML hợp lệ.");
                return;
            }

            var result = MessageBox.Show(
                "Bạn có muốn nạp XML này vào TreeView không?\n\n" + xmlPath,
                "Xác nhận nạp XML",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                LoadXmlFullToTreeView(xmlPath);
            }
        }
    }

}
