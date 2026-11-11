using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

namespace SheetSet_File_DST_Converter
{
    /// <summary>
    /// Xem cấu trúc XML thô của sheet set dạng TreeView (ảnh chụp tại thời điểm mở cửa sổ).
    /// Thuộc tính hiện chữ xanh lá, nội dung văn bản hiện chữ xanh dương.
    /// </summary>
    public sealed class XmlTreeForm : Form
    {
        private const int InitialExpandDepth = 3;

        private readonly TreeView tree;
        private readonly Font boldFont;

        public XmlTreeForm(XmlDocument doc, string title)
        {
            if (doc == null || doc.DocumentElement == null)
                throw new ArgumentException("Không có dữ liệu XML để hiển thị.", nameof(doc));

            Text = "Cấu trúc XML - " + title + "  (ảnh chụp lúc mở)";
            Size = new Size(900, 650);
            MinimumSize = new Size(500, 350);
            StartPosition = FormStartPosition.CenterParent;

            tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            boldFont = new Font(tree.Font, FontStyle.Bold);

            var toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            toolStrip.Items.Add(new ToolStripButton("Mở rộng tất cả", null, (s, e) =>
            {
                tree.BeginUpdate();
                tree.ExpandAll();
                tree.EndUpdate();
            }));
            toolStrip.Items.Add(new ToolStripButton("Thu gọn", null, (s, e) =>
            {
                tree.BeginUpdate();
                tree.CollapseAll();
                if (tree.Nodes.Count > 0) tree.Nodes[0].Expand();
                tree.EndUpdate();
            }));

            // Fill add trước, thanh công cụ add sau
            Controls.Add(tree);
            Controls.Add(toolStrip);

            Load += (s, e) => Populate(doc);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) boldFont.Dispose();
            base.Dispose(disposing);
        }

        private void Populate(XmlDocument doc)
        {
            Cursor = Cursors.WaitCursor;
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();

                var rootNode = new TreeNode(doc.DocumentElement.Name) { NodeFont = boldFont };
                tree.Nodes.Add(rootNode);
                AddXmlNodeRecursive(doc.DocumentElement, rootNode);

                // ExpandAll() rất chậm với sheet set lớn → chỉ mở vài cấp đầu
                ExpandToDepth(rootNode, InitialExpandDepth);
                rootNode.EnsureVisible();
            }
            finally
            {
                tree.EndUpdate();
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
                    treeNode.Nodes.Add(new TreeNode("@" + attr.Name + " = \"" + attr.Value + "\"")
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
                            treeNode.Nodes.Add(new TreeNode("#text = \"" + child.Value.Trim() + "\"")
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
            return string.IsNullOrEmpty(propName) ? element.Name : element.Name + " [" + propName + "]";
        }

        private static void ExpandToDepth(TreeNode node, int depth)
        {
            if (depth <= 0) return;
            node.Expand();
            foreach (TreeNode child in node.Nodes)
                ExpandToDepth(child, depth - 1);
        }
    }
}
