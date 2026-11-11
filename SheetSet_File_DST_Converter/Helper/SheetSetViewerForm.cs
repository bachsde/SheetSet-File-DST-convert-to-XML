using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SheetSet_File_DST_Converter.Helper;

namespace SheetSet_File_DST_Converter
{
    /// <summary>
    /// Cửa sổ xem / sửa Sheet Set giống Sheet Set Manager:
    /// - Trái: cây Sheet Set / Subset / Sheet, chọn nhiều bằng Ctrl / Shift + Click.
    /// - Phải: bảng thuộc tính theo nhóm. Chọn nhiều sheet → sửa giá trị chung cho tất cả.
    /// - Giá trị nhập là "chờ ghi" (chữ đậm) cho tới khi bấm Apply; "Build DST" để lưu file.
    /// - Copy / Paste subset (chỉ cấu trúc subset hoặc kèm sheet), copy được giữa các sheet set.
    /// - Xoá subset / sheet, xoá chỉ sheet trong subset; Hoàn tác (Ctrl+Z) cho Xoá / Paste / Apply.
    /// Form tạo hoàn toàn bằng code (không cần file Designer/resx).
    /// </summary>
    public sealed class SheetSetViewerForm : Form, IPropertyEditSession
    {
        private const int IconSheetSet = 0;
        private const int IconSubset = 1;
        private const int IconSheet = 2;
        private const int IconSheetMissing = 3;

        private readonly SheetSetDocument document;
        private SsmComponent Root { get { return document.Root; } } // dựng lại sau khi paste
        private readonly DstXmlConverter converter;
        private readonly string sourcePath;
        private readonly string sourceDstPath; // null nếu mở từ file XML
        private string sourceDstHash;          // nội dung file .dst lúc mở → phát hiện SSM sửa file trong lúc viewer đang mở
        private readonly string baseTitle;
        private bool dirty; // đã Apply / paste nhưng chưa Build DST
        private string statusNote; // thông báo ngắn trên thanh trạng thái (vd: đã copy subset)

        // Hoàn tác: snapshot XML trước mỗi thao tác Xoá / Paste / Apply
        private const int MaxUndo = 20;
        private readonly List<UndoEntry> undoStack = new List<UndoEntry>();
        private readonly ToolStripButton btnUndo;

        // Giá trị đang chờ Apply
        private readonly Dictionary<SsmProperty, PendingEdit> pending = new Dictionary<SsmProperty, PendingEdit>();

        // Chọn nhiều
        private readonly List<TreeNode> selection = new List<TreeNode>();
        private TreeNode anchorNode;
        private bool suppressAfterSelect;

        // Tránh hiện cùng một thông báo lỗi N lần khi sửa N sheet cùng lúc
        private string lastRejectedValue;
        private int lastRejectTick;

        private readonly SplitContainer split;
        private readonly TreeView tree;
        private readonly PropertyGrid grid;
        private readonly Button btnApply;
        private readonly Button btnDiscard;
        private readonly Label lblPending;
        private readonly ToolStripTextBox txtFilter;
        private readonly ToolStripStatusLabel lblStatus;
        private readonly ImageList icons;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem miOpenDwg;
        private readonly ToolStripMenuItem miOpenFolder;
        private readonly ToolStripMenuItem miSelectSheets;
        private readonly ToolStripMenuItem miCopySubset;
        private readonly ToolStripMenuItem miCopySubsetWithSheets;
        private readonly ToolStripMenuItem miPasteSubset;
        private readonly ToolStripMenuItem miDelete;
        private readonly ToolStripMenuItem miDeleteSheetsInside;
        private readonly ToolStripMenuItem miCopyName;
        private readonly ToolStripMenuItem miCopyPath;
        private readonly System.Windows.Forms.Timer filterTimer;
        private Font rootFont;

        /// <param name="sourceDstPath">File .dst gốc (gợi ý khi Build DST); null nếu mở từ XML.</param>
        /// <param name="sourceDstHash">DstFileGuard.ComputeHash của file .dst lúc đọc; null nếu mở từ XML.</param>
        public SheetSetViewerForm(SheetSetDocument document, DstXmlConverter converter,
                                  string sourcePath, string sourceDstPath, string sourceDstHash)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            this.document = document;
            this.converter = converter;
            this.sourcePath = sourcePath;
            this.sourceDstPath = sourceDstPath;
            this.sourceDstHash = sourceDstHash;

            baseTitle = "Sheet Set Viewer - " + Root.DisplayText + "   [" + Path.GetFileName(sourcePath) + "]";
            Text = baseTitle;
            Size = new Size(1100, 720);
            MinimumSize = new Size(700, 450);
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            icons = CreateIcons();

            // ===== Tree (trái)
            tree = new TreeView
            {
                Dock = DockStyle.Fill,
                ImageList = icons,
                HideSelection = false,
                ShowNodeToolTips = true,
                ItemHeight = 20
            };
            tree.BeforeSelect += Tree_BeforeSelect;
            tree.AfterSelect += Tree_AfterSelect;
            tree.NodeMouseClick += Tree_NodeMouseClick;
            tree.NodeMouseDoubleClick += Tree_NodeMouseDoubleClick;
            tree.KeyDown += Tree_KeyDown;

            // ===== PropertyGrid + Apply / Huỷ (phải)
            grid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                PropertySort = PropertySort.Categorized,
                ToolbarVisible = true,
                HelpVisible = true
            };

            btnApply = new Button { Text = "Apply", Width = 90, Height = 28, Enabled = false };
            btnApply.Font = new Font(btnApply.Font, FontStyle.Bold);
            btnApply.Click += (s, e) => ApplyPending();

            btnDiscard = new Button { Text = "Huỷ", Width = 90, Height = 28, Enabled = false };
            btnDiscard.Click += (s, e) => DiscardPending();

            lblPending = new Label { AutoSize = true, Margin = new Padding(3, 9, 12, 3), ForeColor = Color.DarkGoldenrod };

            var buttonBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 38,
                Padding = new Padding(4, 4, 4, 4)
            };
            buttonBar.Controls.Add(btnDiscard);
            buttonBar.Controls.Add(btnApply);
            buttonBar.Controls.Add(lblPending);

            split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(tree);
            split.Panel2.Controls.Add(grid);      // Fill add trước
            split.Panel2.Controls.Add(buttonBar); // Bottom add sau

            // ===== Context menu
            miOpenDwg = new ToolStripMenuItem("Mở DWG", null, (s, e) => OpenDwg());
            miOpenFolder = new ToolStripMenuItem("Mở thư mục chứa DWG", null, (s, e) => OpenFolder());
            miSelectSheets = new ToolStripMenuItem("Chọn tất cả sheet bên trong", null, (s, e) => SelectAllSheetsUnder(PrimaryNode));
            miCopySubset = new ToolStripMenuItem("Copy subset (chỉ subset)", null, (s, e) => CopySubset(false))
            {
                ToolTipText = "Copy subset này và các subset con, không kèm sheet"
            };
            miCopySubsetWithSheets = new ToolStripMenuItem("Copy subset kèm sheet", null, (s, e) => CopySubset(true))
            {
                ToolTipText = "Copy subset này, các subset con và toàn bộ sheet bên trong"
            };
            miPasteSubset = new ToolStripMenuItem("Paste subset vào đây", null, (s, e) => PasteSubset());
            miDelete = new ToolStripMenuItem("Xoá", null, (s, e) => DeleteSelected())
            {
                ShortcutKeyDisplayString = "Del"
            };
            miDeleteSheetsInside = new ToolStripMenuItem("Xoá tất cả sheet bên trong (giữ subset)", null,
                (s, e) => DeleteSheetsInside());
            miCopyName = new ToolStripMenuItem("Copy tên vào clipboard", null, (s, e) => CopySelectedNames());
            miCopyPath = new ToolStripMenuItem("Copy đường dẫn DWG", null, (s, e) => CopyText(PrimaryComponent?.DwgPath));
            miOpenDwg.Font = new Font(miOpenDwg.Font, FontStyle.Bold); // hành động mặc định khi double-click

            menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[]
            {
                miOpenDwg, miOpenFolder, miSelectSheets, new ToolStripSeparator(),
                miCopySubset, miCopySubsetWithSheets, miPasteSubset, new ToolStripSeparator(),
                miDelete, miDeleteSheetsInside, new ToolStripSeparator(),
                miCopyName, miCopyPath
            });
            menu.Opening += Menu_Opening;
            tree.ContextMenuStrip = menu;

            // ===== Toolbar
            var btnBuild = new ToolStripButton("Build DST...", null, (s, e) => BuildDst())
            {
                ToolTipText = "Lưu các thay đổi thành file .dst (Ctrl+S)"
            };
            btnBuild.Font = new Font(btnBuild.Font, FontStyle.Bold);

            txtFilter = new ToolStripTextBox { Width = 240, ToolTipText = "Lọc theo số hiệu, tên sheet, tên subset hoặc mô tả (Ctrl+F)" };
            txtFilter.TextChanged += (s, e) => { filterTimer.Stop(); filterTimer.Start(); };

            var toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            btnUndo = new ToolStripButton("Hoàn tác", null, (s, e) => Undo())
            {
                Enabled = false,
                ToolTipText = "Hoàn tác (Ctrl+Z)"
            };

            toolStrip.Items.Add(btnBuild);
            toolStrip.Items.Add(btnUndo);
            toolStrip.Items.Add(new ToolStripButton("Xoá", null, (s, e) => DeleteSelected())
            {
                ToolTipText = "Xoá các subset / sheet đang chọn (Del)"
            });
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripButton("Mở rộng tất cả", null, (s, e) => ExpandAll()));
            toolStrip.Items.Add(new ToolStripButton("Thu gọn", null, (s, e) => CollapseToSubsets()));
            toolStrip.Items.Add(new ToolStripButton("Chọn tất cả sheet", null, (s, e) => SelectAllVisibleSheets())
            {
                ToolTipText = "Chọn mọi sheet đang hiện trên cây (Ctrl+A)"
            });
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("Tìm:"));
            toolStrip.Items.Add(txtFilter);
            toolStrip.Items.Add(new ToolStripButton("✕", null, (s, e) => txtFilter.Text = "") { ToolTipText = "Xoá bộ lọc" });

            // ===== Status bar
            lblStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(lblStatus);

            // Thứ tự Add quan trọng: control Fill phải add trước để các thanh Top/Bottom dock trước
            Controls.Add(split);
            Controls.Add(toolStrip);
            Controls.Add(statusStrip);

            filterTimer = new System.Windows.Forms.Timer { Interval = 300 };
            filterTimer.Tick += (s, e) => { filterTimer.Stop(); BuildTree(txtFilter.Text.Trim()); };

            Load += (s, e) =>
            {
                split.SplitterDistance = Math.Min(400, Math.Max(150, ClientSize.Width / 3));
                if (DstFileGuard.IsAutoCadRunning())
                    statusNote = "AutoCAD đang chạy — đóng sheet set trong SSM trước khi Build DST";
                BuildTree("");
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                filterTimer.Dispose();
                menu.Dispose();
                icons.Dispose();
                if (rootFont != null) rootFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (!ResolvePending("đóng cửa sổ"))
                {
                    e.Cancel = true;
                    base.OnFormClosing(e);
                    return;
                }

                if (dirty)
                {
                    var answer = MessageBox.Show(this,
                        "Có thay đổi chưa được build thành DST.\n\nBuild DST trước khi đóng?",
                        "Sheet Set Viewer", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                    if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !BuildDst()))
                        e.Cancel = true;
                }
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                BuildDst();
                return true;
            }
            if (tree.Focused && keyData == Keys.Delete)
            {
                DeleteSelected();
                return true;
            }
            if (tree.Focused && keyData == (Keys.Control | Keys.Z))
            {
                Undo();
                return true;
            }
            if (tree.Focused && keyData == (Keys.Control | Keys.C) && PrimaryComponent?.Kind == SsmNodeKind.Subset)
            {
                CopySubset(true);
                return true;
            }
            if (tree.Focused && keyData == (Keys.Control | Keys.V) && SubsetClipboard.Current != null)
            {
                PasteSubset();
                return true;
            }
            if (keyData == (Keys.Control | Keys.F))
            {
                txtFilter.Focus();
                txtFilter.SelectAll();
                return true;
            }
            if (keyData == Keys.Escape && txtFilter.Focused && txtFilter.Text.Length > 0)
            {
                txtFilter.Text = "";
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        #region Tree – dựng cây

        private void BuildTree(string filter)
        {
            var previous = SelectedComponents.ToList();
            var previousPrimary = PrimaryComponent;

            tree.BeginUpdate();
            try
            {
                selection.Clear();
                anchorNode = null;

                SetNativeSelected(null);
                tree.Nodes.Clear();

                var rootNode = CreateNode(Root, filter) ?? MakeNode(Root); // gốc luôn hiện
                tree.Nodes.Add(rootNode);

                if (string.IsNullOrEmpty(filter))
                {
                    rootNode.Expand();
                    foreach (TreeNode n in rootNode.Nodes) n.Expand();
                }
                else
                {
                    rootNode.ExpandAll();
                }

                // Giữ lại lựa chọn cũ (những mục vẫn còn hiện sau khi lọc)
                foreach (var comp in previous)
                {
                    var node = FindNode(rootNode, comp);
                    if (node != null) selection.Add(node);
                }
                if (selection.Count == 0) selection.Add(rootNode);

                var primary = FindNode(rootNode, previousPrimary);
                if (primary == null || !selection.Contains(primary)) primary = selection[selection.Count - 1];
                anchorNode = primary;

                SetNativeSelected(primary);
                primary.EnsureVisible();
                RefreshSelectionVisuals();
            }
            finally
            {
                tree.EndUpdate();
            }

            UpdateGrid();
            UpdateStatus();
        }

        /// <summary>
        /// Trả null nếu node và toàn bộ con không khớp bộ lọc.
        /// Subset khớp tên → hiện toàn bộ sheet bên trong.
        /// </summary>
        private TreeNode CreateNode(SsmComponent comp, string filter)
        {
            bool filtering = !string.IsNullOrEmpty(filter);
            bool selfMatch = filtering && Matches(comp, filter);
            string childFilter = selfMatch ? "" : filter;

            var node = MakeNode(comp);
            foreach (var child in comp.Children)
            {
                var childNode = CreateNode(child, childFilter);
                if (childNode != null) node.Nodes.Add(childNode);
            }

            if (!filtering || selfMatch || node.Nodes.Count > 0) return node;
            return null;
        }

        private TreeNode MakeNode(SsmComponent comp)
        {
            var node = new TreeNode(comp.DisplayText) { Tag = comp };

            int icon;
            switch (comp.Kind)
            {
                case SsmNodeKind.SheetSet:
                    icon = IconSheetSet;
                    if (rootFont == null) rootFont = new Font(tree.Font, FontStyle.Bold);
                    node.NodeFont = rootFont;
                    node.ToolTipText = comp.Description;
                    break;

                case SsmNodeKind.Subset:
                    icon = IconSubset;
                    node.ToolTipText = comp.Description;
                    break;

                default:
                    icon = IsMissingDwg(comp) ? IconSheetMissing : IconSheet;
                    node.ToolTipText = IsMissingDwg(comp)
                        ? "Không tìm thấy DWG:\n" + comp.DwgPath
                        : (string.IsNullOrEmpty(comp.Description) ? comp.DwgPath : comp.Description + "\n" + comp.DwgPath);
                    break;
            }

            node.ForeColor = NormalForeColor(comp);
            node.ImageIndex = icon;
            node.SelectedImageIndex = icon;
            return node;
        }

        private static bool IsMissingDwg(SsmComponent comp)
        {
            return comp.Kind == SsmNodeKind.Sheet && !string.IsNullOrEmpty(comp.DwgPath) && !comp.DwgExists;
        }

        private static Color NormalForeColor(SsmComponent comp)
        {
            return comp != null && IsMissingDwg(comp) ? Color.Firebrick : Color.Empty;
        }

        private static bool Matches(SsmComponent comp, string filter)
        {
            return Contains(comp.DisplayText, filter) || Contains(comp.Description, filter);
        }

        private static bool Contains(string text, string filter)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static TreeNode FindNode(TreeNode node, SsmComponent target)
        {
            if (target == null) return null;
            if (ReferenceEquals(node.Tag, target)) return node;
            foreach (TreeNode child in node.Nodes)
            {
                var found = FindNode(child, target);
                if (found != null) return found;
            }
            return null;
        }

        private void ExpandAll()
        {
            tree.BeginUpdate();
            tree.ExpandAll();
            tree.SelectedNode?.EnsureVisible();
            tree.EndUpdate();
        }

        private void CollapseToSubsets()
        {
            if (tree.Nodes.Count == 0) return;
            tree.BeginUpdate();
            tree.CollapseAll();
            tree.Nodes[0].Expand();
            tree.EndUpdate();
        }

        #endregion

        #region Tree – chọn nhiều (Ctrl / Shift + Click)

        private TreeNode PrimaryNode
        {
            get { return tree.SelectedNode ?? selection.LastOrDefault(); }
        }

        private SsmComponent PrimaryComponent
        {
            get { return PrimaryNode?.Tag as SsmComponent; }
        }

        private IEnumerable<SsmComponent> SelectedComponents
        {
            get { return selection.Select(n => n.Tag as SsmComponent).Where(c => c != null); }
        }

        private void Tree_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            // Ctrl/Shift + Click: tự xử lý trong NodeMouseClick, không để TreeView đổi lựa chọn
            if (e.Action == TreeViewAction.ByMouse && (ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
                e.Cancel = true;
        }

        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (suppressAfterSelect || e.Node == null) return;
            SelectSingle(e.Node); // click thường hoặc phím mũi tên
        }

        private void Tree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node == null) return;

            // Bỏ qua click vào dấu +/- mở rộng
            var hit = tree.HitTest(e.Location);
            if (hit.Location == TreeViewHitTestLocations.PlusMinus) return;

            if (e.Button == MouseButtons.Right)
            {
                // Chuột phải vào mục đang chọn → giữ nguyên lựa chọn nhiều
                if (!selection.Contains(e.Node)) SelectSingle(e.Node);
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            var mods = ModifierKeys;
            if ((mods & Keys.Shift) != 0 && anchorNode != null)
            {
                SelectRange(anchorNode, e.Node, (mods & Keys.Control) != 0);
            }
            else if ((mods & Keys.Control) != 0)
            {
                ToggleNode(e.Node);
            }
            else if (!(selection.Count == 1 && selection[0] == e.Node))
            {
                SelectSingle(e.Node);
            }
        }

        private void Tree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAllVisibleSheets();
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        private void SelectSingle(TreeNode node)
        {
            selection.Clear();
            selection.Add(node);
            anchorNode = node;
            SetNativeSelected(node);
            OnSelectionChanged();
        }

        private void ToggleNode(TreeNode node)
        {
            if (selection.Contains(node))
            {
                if (selection.Count == 1) return; // luôn giữ ít nhất 1 mục
                selection.Remove(node);
                if (tree.SelectedNode == node) SetNativeSelected(selection[selection.Count - 1]);
            }
            else
            {
                selection.Add(node);
                SetNativeSelected(node);
            }
            anchorNode = node;
            OnSelectionChanged();
        }

        private void SelectRange(TreeNode from, TreeNode to, bool additive)
        {
            var visible = VisibleNodes().ToList();
            int a = visible.IndexOf(from);
            int b = visible.IndexOf(to);
            if (a < 0 || b < 0)
            {
                SelectSingle(to);
                return;
            }

            if (!additive) selection.Clear();
            for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                if (!selection.Contains(visible[i])) selection.Add(visible[i]);

            SetNativeSelected(to);
            OnSelectionChanged();
        }

        private void SelectAllVisibleSheets()
        {
            var sheets = VisibleNodes().Where(n => (n.Tag as SsmComponent)?.Kind == SsmNodeKind.Sheet).ToList();
            SelectNodes(sheets);
        }

        private void SelectAllSheetsUnder(TreeNode parent)
        {
            if (parent == null) return;
            parent.ExpandAll();
            var sheets = Descendants(parent).Where(n => (n.Tag as SsmComponent)?.Kind == SsmNodeKind.Sheet).ToList();
            SelectNodes(sheets);
        }

        private void SelectNodes(List<TreeNode> nodes)
        {
            if (nodes.Count == 0) return;
            selection.Clear();
            selection.AddRange(nodes);
            anchorNode = nodes[0];
            SetNativeSelected(nodes[nodes.Count - 1]);
            OnSelectionChanged();
        }

        private void SetNativeSelected(TreeNode node)
        {
            suppressAfterSelect = true;
            try { tree.SelectedNode = node; }
            finally { suppressAfterSelect = false; }
        }

        private void OnSelectionChanged()
        {
            RefreshSelectionVisuals();
            UpdateGrid();
            UpdateStatus();
        }

        /// <summary>Tô màu các mục được chọn (TreeView chỉ tự tô mục SelectedNode).</summary>
        private void RefreshSelectionVisuals()
        {
            if (tree.Nodes.Count == 0) return;

            tree.BeginUpdate();
            foreach (var node in Descendants(tree.Nodes[0]).Concat(new[] { tree.Nodes[0] }))
            {
                bool highlighted = selection.Contains(node) && node != tree.SelectedNode;
                node.BackColor = highlighted ? SystemColors.Highlight : Color.Empty;
                node.ForeColor = highlighted ? SystemColors.HighlightText : NormalForeColor(node.Tag as SsmComponent);
            }
            tree.EndUpdate();
        }

        private IEnumerable<TreeNode> VisibleNodes()
        {
            if (tree.Nodes.Count == 0) yield break;
            for (var n = tree.Nodes[0]; n != null; n = n.NextVisibleNode)
                yield return n;
        }

        private static IEnumerable<TreeNode> Descendants(TreeNode node)
        {
            foreach (TreeNode child in node.Nodes)
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        private void Tree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0) return;

            var comp = e.Node?.Tag as SsmComponent;
            if (comp != null && comp.Kind == SsmNodeKind.Sheet)
                OpenDwg(); // giống SSM: double-click sheet → mở bản vẽ
        }

        #endregion

        #region PropertyGrid & giá trị chờ Apply

        private void UpdateGrid()
        {
            var bags = SelectedComponents.Select(c => (object)new PropertyBag(c, this)).ToArray();

            if (bags.Length == 0) grid.SelectedObject = null;
            else if (bags.Length == 1) grid.SelectedObject = bags[0];
            else grid.SelectedObjects = bags; // PropertyGrid tự gộp các thuộc tính chung
        }

        string IPropertyEditSession.GetValue(SsmProperty prop)
        {
            PendingEdit edit;
            return pending.TryGetValue(prop, out edit) ? edit.Value : prop.Value;
        }

        bool IPropertyEditSession.HasPending(SsmProperty prop)
        {
            return pending.ContainsKey(prop);
        }

        /// <summary>Gọi từ PropertyGrid khi người dùng nhập giá trị (mỗi mục được chọn gọi một lần).</summary>
        bool IPropertyEditSession.SetPending(SsmComponent comp, SsmProperty prop, string value)
        {
            value = value ?? "";

            string error = ValidateValue(value);
            if (error != null)
            {
                // Sửa N sheet → hàm này bị gọi N lần liên tiếp; chỉ báo lỗi 1 lần
                bool justShown = value == lastRejectedValue && unchecked(Environment.TickCount - lastRejectTick) < 500;
                if (!justShown)
                {
                    MessageBox.Show(this, error, "Không thể dùng giá trị này",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                lastRejectedValue = value;
                lastRejectTick = Environment.TickCount;
                return false;
            }

            if (value == prop.Value)
                pending.Remove(prop); // nhập lại đúng giá trị cũ → bỏ chờ
            else
                pending[prop] = new PendingEdit(comp, prop, value);

            UpdatePendingUi();
            return true;
        }

        private string ValidateValue(string value)
        {
            if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                return "Giá trị không được chứa xuống dòng.";

            // Chặn ngay ký tự chưa có trong dictionary, tránh tới lúc Build DST mới báo lỗi
            string error;
            return converter.CanEncodeText(value, out error) ? null : error;
        }

        private void UpdatePendingUi()
        {
            int n = pending.Count;
            btnApply.Enabled = n > 0;
            btnDiscard.Enabled = n > 0;

            if (n == 0)
            {
                lblPending.Text = "";
            }
            else
            {
                int items = pending.Values.Select(p => p.Component).Distinct().Count();
                lblPending.Text = $"{n} giá trị chờ ghi ({items} mục) — bấm Apply để xác nhận";
            }
            UpdateStatus();
        }

        /// <summary>Ghi các giá trị chờ vào sheet set. Trả false nếu người dùng huỷ.</summary>
        private bool ApplyPending()
        {
            if (pending.Count == 0) return true;

            // Kiểm tra trùng số hiệu sheet sau khi áp dụng
            var duplicates = FindDuplicateNumbersAfterApply();
            if (duplicates.Count > 0)
            {
                var answer = MessageBox.Show(this,
                    "Các số hiệu sau sẽ bị trùng giữa nhiều sheet:\n  • " +
                    string.Join("\n  • ", duplicates.Take(15)) +
                    (duplicates.Count > 15 ? $"\n  … và {duplicates.Count - 15} số khác" : "") +
                    "\n\nVẫn Apply?",
                    "Trùng số hiệu", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return false;
            }

            PushUndo($"Apply {pending.Count} giá trị");

            foreach (var edit in pending.Values)
                document.ApplyEdit(edit.Component, edit.Property, edit.Value);

            // Cập nhật chữ trên cây (Number / Title / Name có thể đã đổi)
            if (tree.Nodes.Count > 0)
            {
                foreach (var comp in pending.Values.Select(p => p.Component).Distinct())
                {
                    var node = FindNode(tree.Nodes[0], comp);
                    if (node != null) node.Text = comp.DisplayText;
                }
            }

            pending.Clear();
            dirty = true;
            Text = baseTitle + "  *";

            grid.Refresh();
            UpdatePendingUi();
            return true;
        }

        private void DiscardPending()
        {
            pending.Clear();
            grid.Refresh();
            UpdatePendingUi();
        }

        /// <summary>Trả về các số hiệu trùng nhau (ít nhất 1 sheet trong đó đang có thay đổi chờ).</summary>
        private List<string> FindDuplicateNumbersAfterApply()
        {
            var changedNumbers = pending.Values
                .Where(p => p.Component.Kind == SsmNodeKind.Sheet && !p.Property.IsCustom && p.Property.PropName == "Number")
                .ToDictionary(p => p.Component, p => p.Value);

            if (changedNumbers.Count == 0) return new List<string>();

            var all = new List<KeyValuePair<SsmComponent, string>>();
            CollectSheetNumbers(Root, changedNumbers, all);

            return all
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .GroupBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
                .Where(g => g.Count() > 1 && g.Any(kv => changedNumbers.ContainsKey(kv.Key)))
                .Select(g => $"\"{g.Key}\" ({g.Count()} sheet)")
                .ToList();
        }

        private static void CollectSheetNumbers(SsmComponent node, Dictionary<SsmComponent, string> changed,
                                                List<KeyValuePair<SsmComponent, string>> result)
        {
            if (node.Kind == SsmNodeKind.Sheet)
            {
                string number;
                if (!changed.TryGetValue(node, out number)) number = node.Number;
                result.Add(new KeyValuePair<SsmComponent, string>(node, number));
            }
            foreach (var child in node.Children)
                CollectSheetNumbers(child, changed, result);
        }

        /// <summary>
        /// Trước khi Build / đóng: hỏi xử lý giá trị chưa Apply.
        /// Trả false nếu người dùng chọn Cancel.
        /// </summary>
        private bool ResolvePending(string action)
        {
            if (pending.Count == 0) return true;

            var answer = MessageBox.Show(this,
                $"Có {pending.Count} giá trị chưa Apply.\n\nApply trước khi {action}?\n" +
                "(No = bỏ các giá trị chưa Apply)",
                "Sheet Set Viewer", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) return ApplyPending();

            DiscardPending();
            return true;
        }

        #endregion

        #region Copy / Paste subset

        private void CopySubset(bool includeSheets)
        {
            var comp = PrimaryComponent;
            if (comp == null || comp.Kind != SsmNodeKind.Subset) return;

            try
            {
                var clip = SheetSetDocument.CloneSubset(comp, includeSheets);
                SubsetClipboard.Set(clip, comp.Name, includeSheets);

                statusNote = $"Đã copy \"{comp.Name}\" ({SubsetClipboard.Current.Summary}) — chuột phải subset / sheet set đích → Paste";
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Lỗi copy subset", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PasteSubset()
        {
            var target = PrimaryComponent;
            var clip = SubsetClipboard.Current;
            if (target == null || target.Kind == SsmNodeKind.Sheet || clip == null) return;

            // Paste dựng lại toàn bộ cây → phải xử lý giá trị chờ Apply trước
            if (!ResolvePending("paste")) return;

            try
            {
                string name = InputDialog.Show(this, "Paste subset",
                    $"Paste \"{clip.Name}\" ({clip.Summary})\nvào: {target.DisplayText}\n\nTên subset mới:",
                    SuggestSubsetName(target, clip.Name));
                if (name == null) return;

                name = name.Trim();
                if (name.Length == 0)
                {
                    MessageBox.Show(this, "Tên subset không được để trống.", "Paste subset",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string error = ValidateValue(name);
                if (error != null)
                {
                    MessageBox.Show(this, error, "Không thể dùng tên này", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (clip.SheetCount > 0)
                {
                    var answer = MessageBox.Show(this,
                        $"Bản copy có {clip.SheetCount} sheet.\n\n" +
                        "Các sheet copy vẫn trỏ tới CÙNG file DWG / layout với sheet gốc " +
                        "(giống nhau về bản vẽ, chỉ khác ID trong sheet set).\n" +
                        "Bạn nên đổi Number / Title cho các sheet này sau khi paste.\n\nTiếp tục paste?",
                        "Paste subset kèm sheet", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) return;
                }

                PushUndo($"Paste \"{name}\"");
                var newNode = document.PasteSubset(clip.Node, target, name);

                dirty = true;
                Text = baseTitle + "  *";
                statusNote = $"Đã paste \"{name}\" ({clip.Summary})";

                // Dựng lại cây (bỏ lọc để chắc chắn thấy subset mới) rồi chọn subset mới
                filterTimer.Stop();
                if (txtFilter.Text.Length > 0)
                {
                    txtFilter.Text = "";
                    filterTimer.Stop();
                }
                BuildTree("");

                var newTreeNode = FindNodeByXml(tree.Nodes[0], newNode);
                if (newTreeNode != null)
                {
                    newTreeNode.Expand();
                    SelectSingle(newTreeNode);
                    newTreeNode.EnsureVisible();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Lỗi paste subset", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Trùng tên với subset con đã có → thêm " - Copy", " - Copy (2)", ...</summary>
        private static string SuggestSubsetName(SsmComponent target, string name)
        {
            var existing = new HashSet<string>(
                target.Children.Where(c => c.Kind == SsmNodeKind.Subset).Select(c => c.Name ?? ""),
                StringComparer.CurrentCultureIgnoreCase);

            if (!existing.Contains(name)) return name;

            string candidate = name + " - Copy";
            for (int i = 2; existing.Contains(candidate); i++)
                candidate = $"{name} - Copy ({i})";
            return candidate;
        }

        private static TreeNode FindNodeByXml(TreeNode node, System.Xml.XmlNode xml)
        {
            var comp = node.Tag as SsmComponent;
            if (comp != null && ReferenceEquals(comp.Node, xml)) return node;
            foreach (TreeNode child in node.Nodes)
            {
                var found = FindNodeByXml(child, xml);
                if (found != null) return found;
            }
            return null;
        }

        #endregion

        #region Delete & Undo

        private void DeleteSelected()
        {
            var selected = SelectedComponents.ToList();
            var targets = selected.Where(c => c.Kind != SsmNodeKind.SheetSet).ToList();

            if (targets.Count == 0)
            {
                if (selected.Count > 0)
                    MessageBox.Show(this, "Không xoá được sheet set gốc.", "Xoá",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!ResolvePending("xoá")) return;

            try
            {
                // Bỏ các mục nằm trong mục khác cũng bị xoá
                var topNodes = new HashSet<System.Xml.XmlNode>(SheetSetDocument.TopLevelNodes(targets));
                var top = targets.Where(c => topNodes.Contains(c.Node)).ToList();

                int subsets = top.Sum(c => c.CountSubsets());
                int sheets = top.Sum(c => c.CountSheets());
                int externalRefs = document.CountExternalReferences(topNodes);

                var msg = new StringBuilder();
                msg.AppendLine($"Xoá {top.Count} mục sau?");
                foreach (var c in top.Take(15))
                    msg.AppendLine("  • " + (c.Kind == SsmNodeKind.Subset ? "[Subset] " : "") + c.DisplayText);
                if (top.Count > 15) msg.AppendLine($"  … và {top.Count - 15} mục khác");
                msg.AppendLine();
                msg.AppendLine($"Tổng cộng: {subsets} subset, {sheets} sheet sẽ bị xoá khỏi sheet set.");
                msg.AppendLine("File DWG không bị xoá.");
                if (externalRefs > 0)
                {
                    msg.AppendLine();
                    msg.AppendLine($"Cảnh báo: có {externalRefs} chỗ khác trong sheet set (vd: sheet selection, " +
                                   "danh mục sheet) đang tham chiếu tới các mục này.");
                }
                msg.AppendLine();
                msg.Append("Có thể Hoàn tác (Ctrl+Z) trước khi Build DST.");

                if (MessageBox.Show(this, msg.ToString(), "Xác nhận xoá",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                // Sau khi xoá: chọn subset / sheet set chứa mục đầu tiên
                var parentXml = top[0].Node.ParentNode;

                PushUndo($"Xoá {top.Count} mục");
                document.DeleteComponents(top);

                AfterStructureChanged($"Đã xoá {subsets} subset, {sheets} sheet", parentXml);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Lỗi xoá", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteSheetsInside()
        {
            var container = PrimaryComponent;
            if (container == null || container.Kind == SsmNodeKind.Sheet) return;

            int sheets = container.CountSheets();
            if (sheets == 0) return;

            if (!ResolvePending("xoá")) return;

            try
            {
                var sheetNodes = container.Node.SelectNodes(".//AcSmSheet").Cast<System.Xml.XmlNode>().ToList();
                int externalRefs = document.CountExternalReferences(sheetNodes);

                string where = container.Kind == SsmNodeKind.SheetSet
                    ? "TOÀN BỘ sheet set"
                    : $"subset \"{container.DisplayText}\" (kể cả các subset con)";

                string msg = $"Xoá {sheets} sheet trong {where}?\n\n" +
                             "Các subset được giữ nguyên. File DWG không bị xoá.";
                if (externalRefs > 0)
                    msg += $"\n\nCảnh báo: có {externalRefs} chỗ khác trong sheet set đang tham chiếu tới các sheet này.";
                msg += "\n\nCó thể Hoàn tác (Ctrl+Z) trước khi Build DST.";

                if (MessageBox.Show(this, msg, "Xác nhận xoá sheet",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                var containerXml = container.Node;

                PushUndo($"Xoá {sheets} sheet trong \"{container.DisplayText}\"");
                int removed = document.DeleteSheetsInside(container);

                AfterStructureChanged($"Đã xoá {removed} sheet, giữ nguyên subset", containerXml);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Lỗi xoá", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PushUndo(string label)
        {
            undoStack.Add(new UndoEntry(label, document.CreateSnapshot()));
            if (undoStack.Count > MaxUndo) undoStack.RemoveAt(0);
            UpdateUndoUi();
        }

        private void Undo()
        {
            if (undoStack.Count == 0) return;

            // Giá trị chờ Apply gắn với cây hiện tại → bỏ trước khi hoàn tác
            if (pending.Count > 0)
            {
                if (MessageBox.Show(this,
                        $"Có {pending.Count} giá trị chưa Apply sẽ bị bỏ khi hoàn tác.\n\nTiếp tục?",
                        "Hoàn tác", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                DiscardPending();
            }

            try
            {
                var entry = undoStack[undoStack.Count - 1];
                undoStack.RemoveAt(undoStack.Count - 1);

                document.RestoreSnapshot(entry.Snapshot);
                AfterStructureChanged("Đã hoàn tác: " + entry.Label, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Lỗi hoàn tác", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdateUndoUi();
        }

        private void UpdateUndoUi()
        {
            btnUndo.Enabled = undoStack.Count > 0;
            btnUndo.ToolTipText = undoStack.Count > 0
                ? $"Hoàn tác: {undoStack[undoStack.Count - 1].Label} (Ctrl+Z)"
                : "Hoàn tác (Ctrl+Z)";
        }

        /// <summary>Sau Xoá / Hoàn tác: đánh dấu có thay đổi, dựng lại cây, chọn mục cần thiết.</summary>
        private void AfterStructureChanged(string note, System.Xml.XmlNode selectXml)
        {
            dirty = true;
            Text = baseTitle + "  *";
            statusNote = note;

            filterTimer.Stop();
            BuildTree(txtFilter.Text.Trim());

            if (selectXml != null && tree.Nodes.Count > 0)
            {
                var node = FindNodeByXml(tree.Nodes[0], selectXml);
                if (node != null)
                {
                    SelectSingle(node);
                    node.EnsureVisible();
                }
            }
            UpdateStatus();
        }

        private sealed class UndoEntry
        {
            public UndoEntry(string label, byte[] snapshot)
            {
                Label = label;
                Snapshot = snapshot;
            }

            public string Label { get; }
            public byte[] Snapshot { get; }
        }

        #endregion

        #region Build DST

        /// <summary>Mã hoá XML hiện tại thành DST và lưu file. Trả true nếu đã lưu.</summary>
        private bool BuildDst()
        {
            if (!ResolvePending("build DST")) return false;

            try
            {
                // 1. XML → byte (giữ định dạng gốc) → kiểm tra → DST
                bool bomRemoved;
                byte[] xml = converter.PrepareXmlForEncoding(document.Serialize(), out bomRemoved);
                byte[] dst = converter.XmlToDst(xml);

                // 2. Chọn nơi lưu: mặc định ghi đè file .dst gốc (có backup)
                string defaultPath = sourceDstPath ?? Path.ChangeExtension(sourcePath, ".dst");
                string dstPath;
                using (var sfd = new SaveFileDialog())
                {
                    sfd.Title = "Build DST";
                    sfd.Filter = "DST files (*.dst)|*.dst|All files (*.*)|*.*";
                    sfd.FileName = Path.GetFileName(defaultPath);
                    string dir = Path.GetDirectoryName(defaultPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        sfd.InitialDirectory = dir;

                    if (sfd.ShowDialog(this) != DialogResult.OK) return false;
                    dstPath = sfd.FileName;
                }

                // 3. Kiểm tra an toàn: file bị khoá / bị SSM sửa trong lúc mở / AutoCAD đang chạy
                if (!ConfirmSafeToWrite(dstPath)) return false;

                // 4. Backup rồi ghi
                string backupPath = null;
                if (File.Exists(dstPath))
                {
                    backupPath = dstPath + ".bak";
                    File.Copy(dstPath, backupPath, true);
                }
                File.WriteAllBytes(dstPath, dst);

                // File gốc giờ là bản mình vừa ghi → lần Build sau không cảnh báo nhầm
                if (DstFileGuard.SamePath(dstPath, sourceDstPath))
                    sourceDstHash = DstFileGuard.ComputeHash(dst);

                dirty = false;
                Text = baseTitle;
                UpdateStatus();

                var msg = new StringBuilder();
                msg.AppendLine("Đã build DST:");
                msg.AppendLine(dstPath);
                if (backupPath != null)
                {
                    msg.AppendLine();
                    msg.AppendLine("File cũ đã được backup:");
                    msg.AppendLine(backupPath);
                }
                if (!document.FormatPreserved)
                {
                    msg.AppendLine();
                    msg.AppendLine("Lưu ý: định dạng XML (khoảng trắng / cách escape) có khác nhỏ so với file gốc; nội dung không đổi.");
                }
                msg.AppendLine();
                msg.Append("Nếu sheet set đang mở trong AutoCAD, hãy đóng rồi mở lại trong Sheet Set Manager để thấy thay đổi.");

                MessageBox.Show(this, msg.ToString(), "Build DST",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 32 || (ex.HResult & 0xFFFF) == 33)
            {
                MessageBox.Show(this,
                    "File DST đang bị khoá — có thể AutoCAD đang mở sheet set này.\n" +
                    "Hãy đóng sheet set trong Sheet Set Manager rồi thử lại.\n\n" + ex.Message,
                    "Không ghi được file DST", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Lỗi Build DST",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        #endregion

        private bool ConfirmSafeToWrite(string dstPath)
        {
            if (!File.Exists(dstPath)) return true; // file mới, không ghi đè gì

            // 1. File đang bị khoá
            if (!DstFileGuard.ConfirmNotLocked(this, dstPath)) return false;

            // 2. File gốc bị sửa từ bên ngoài (thường là SSM lưu) kể từ lúc mở viewer
            if (sourceDstHash != null && DstFileGuard.SamePath(dstPath, sourceDstPath) &&
                DstFileGuard.ComputeFileHash(dstPath) != sourceDstHash)
            {
                var answer = MessageBox.Show(this,
                    "File .dst đã bị chương trình khác thay đổi (thường là Sheet Set Manager lưu lại) " +
                    "kể từ lúc mở viewer.\n\n" +
                    "Build sẽ GHI ĐÈ các thay đổi đó (file hiện tại vẫn được backup .bak).\n\n" +
                    "Muốn giữ thay đổi của SSM: chọn No, đóng viewer, mở lại file rồi làm lại thao tác.\n\n" +
                    "Vẫn ghi đè?",
                    "File đã bị thay đổi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return false;
            }

            // 3. AutoCAD đang chạy → nhắc đóng sheet set
            return DstFileGuard.ConfirmAutoCadClosed(this);
        }

        #region Status bar

        private void UpdateStatus()
        {
            string text = $"{Root.CountSubsets()} subset  •  {Root.CountSheets()} sheet";

            int missing = Root.CountMissingDwg();
            if (missing > 0) text += $"  •  {missing} sheet không tìm thấy DWG (chữ đỏ)";

            string filter = txtFilter.Text.Trim();
            if (!string.IsNullOrEmpty(filter) && tree.Nodes.Count > 0)
            {
                int visibleSheets = VisibleOrCollapsedSheets(tree.Nodes[0]);
                text += $"  •  Đang lọc \"{filter}\": {visibleSheets} sheet";
            }

            if (selection.Count > 1) text += $"  •  Đang chọn {selection.Count} mục";
            if (pending.Count > 0) text += $"  •  {pending.Count} giá trị chờ Apply";
            if (dirty) text += "  •  CÓ THAY ĐỔI CHƯA BUILD DST";
            if (!string.IsNullOrEmpty(statusNote)) text += "  •  " + statusNote;

            lblStatus.Text = text;
        }

        private static int VisibleOrCollapsedSheets(TreeNode node)
        {
            var comp = node.Tag as SsmComponent;
            int n = comp != null && comp.Kind == SsmNodeKind.Sheet ? 1 : 0;
            foreach (TreeNode child in node.Nodes) n += VisibleOrCollapsedSheets(child);
            return n;
        }

        #endregion

        #region Context menu

        private void Menu_Opening(object sender, CancelEventArgs e)
        {
            var comp = PrimaryComponent;
            if (comp == null)
            {
                e.Cancel = true;
                return;
            }

            bool isSheet = comp.Kind == SsmNodeKind.Sheet;
            miOpenDwg.Visible = isSheet;
            miOpenFolder.Visible = isSheet;
            miCopyPath.Visible = isSheet;
            miSelectSheets.Visible = !isSheet;

            miOpenDwg.Enabled = isSheet && comp.DwgExists;
            miOpenFolder.Enabled = isSheet && comp.DwgExists;
            miCopyPath.Enabled = isSheet && !string.IsNullOrEmpty(comp.DwgPath);
            miCopyName.Text = selection.Count > 1 ? $"Copy tên vào clipboard ({selection.Count} mục)" : "Copy tên vào clipboard";

            bool isSubset = comp.Kind == SsmNodeKind.Subset;
            miCopySubset.Visible = isSubset;
            miCopySubsetWithSheets.Visible = isSubset;

            int deletable = SelectedComponents.Count(c => c.Kind != SsmNodeKind.SheetSet);
            miDelete.Enabled = deletable > 0;
            miDelete.Text = selection.Count > 1 ? $"Xoá {deletable} mục đang chọn" : "Xoá";
            miDeleteSheetsInside.Visible = !isSheet;
            miDeleteSheetsInside.Enabled = !isSheet && comp.CountSheets() > 0;

            var clip = SubsetClipboard.Current;
            miPasteSubset.Visible = !isSheet;
            miPasteSubset.Enabled = !isSheet && clip != null;
            miPasteSubset.Text = clip == null
                ? "Paste subset vào đây (chưa copy subset nào)"
                : $"Paste \"{clip.Name}\" vào đây ({clip.Summary})";
        }

        private void OpenDwg()
        {
            var comp = PrimaryComponent;
            if (comp == null || comp.Kind != SsmNodeKind.Sheet) return;

            if (!comp.DwgExists)
            {
                MessageBox.Show(this, "Không tìm thấy file DWG:\n" + comp.DwgPath,
                    "Sheet Set Viewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // UseShellExecute = true để chạy được cả trên .NET Framework lẫn .NET 6+
                Process.Start(new ProcessStartInfo(comp.DwgPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không mở được DWG:\n" + ex.Message,
                    "Sheet Set Viewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenFolder()
        {
            var comp = PrimaryComponent;
            if (comp == null || !comp.DwgExists) return;

            try
            {
                Process.Start("explorer.exe", "/select,\"" + comp.DwgPath + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không mở được thư mục:\n" + ex.Message,
                    "Sheet Set Viewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Copy tên các mục đang chọn, theo thứ tự trên cây, mỗi dòng một mục.</summary>
        private void CopySelectedNames()
        {
            var ordered = VisibleNodes().Where(selection.Contains).Select(n => n.Text);
            CopyText(string.Join(Environment.NewLine, ordered));
        }

        private static void CopyText(string text)
        {
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }

        #endregion

        #region Icons

        private static ImageList CreateIcons()
        {
            var list = new ImageList
            {
                ImageSize = new Size(16, 16),
                ColorDepth = ColorDepth.Depth32Bit
            };

            // 0. Sheet set: bìa hồ sơ xanh
            list.Images.Add(DrawIcon(g =>
            {
                using (var b = new SolidBrush(Color.SteelBlue)) g.FillRectangle(b, 2, 1, 12, 14);
                using (var p = new Pen(Color.White))
                {
                    g.DrawLine(p, 5, 5, 11, 5);
                    g.DrawLine(p, 5, 8, 11, 8);
                }
                using (var p = new Pen(Color.FromArgb(30, 60, 100))) g.DrawRectangle(p, 2, 1, 11, 13);
            }));

            // 1. Subset: thư mục vàng
            list.Images.Add(DrawIcon(g =>
            {
                using (var b = new SolidBrush(Color.FromArgb(230, 180, 60)))
                {
                    g.FillRectangle(b, 1, 3, 6, 3);
                    g.FillRectangle(b, 1, 5, 14, 9);
                }
                using (var p = new Pen(Color.FromArgb(160, 115, 20))) g.DrawRectangle(p, 1, 5, 13, 8);
            }));

            // 2. Sheet: tờ giấy
            list.Images.Add(DrawIcon(g => DrawPage(g, Color.Gray)));

            // 3. Sheet thiếu DWG: tờ giấy + dấu X đỏ
            list.Images.Add(DrawIcon(g =>
            {
                DrawPage(g, Color.Firebrick);
                using (var p = new Pen(Color.Red, 2))
                {
                    g.DrawLine(p, 8, 8, 14, 14);
                    g.DrawLine(p, 14, 8, 8, 14);
                }
            }));

            return list;
        }

        private static void DrawPage(Graphics g, Color border)
        {
            g.FillRectangle(Brushes.White, 3, 1, 10, 14);
            using (var p = new Pen(border)) g.DrawRectangle(p, 3, 1, 9, 13);
            using (var p = new Pen(Color.LightSteelBlue))
            {
                g.DrawLine(p, 5, 4, 10, 4);
                g.DrawLine(p, 5, 7, 10, 7);
                g.DrawLine(p, 5, 10, 10, 10);
            }
        }

        private static Bitmap DrawIcon(Action<Graphics> draw)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                draw(g);
            }
            return bmp;
        }

        #endregion

        private sealed class PendingEdit
        {
            public PendingEdit(SsmComponent component, SsmProperty property, string value)
            {
                Component = component;
                Property = property;
                Value = value;
            }

            public SsmComponent Component { get; }
            public SsmProperty Property { get; }
            public string Value { get; }
        }
    }

    #region Subset clipboard & input dialog

    /// <summary>
    /// Clipboard dùng chung cho mọi cửa sổ viewer → copy subset từ sheet set này paste sang sheet set khác được.
    /// Lưu bản sao tách rời tại thời điểm copy (sửa bản gốc sau đó không ảnh hưởng).
    /// </summary>
    internal sealed class SubsetClipboard
    {
        private SubsetClipboard(System.Xml.XmlElement node, string name, bool withSheets)
        {
            Node = node;
            Name = name;
            WithSheets = withSheets;

            int subsets, sheets;
            SheetSetDocument.CountClip(node, out subsets, out sheets);
            SubsetCount = subsets;
            SheetCount = sheets;
        }

        public static SubsetClipboard Current { get; private set; }

        public static void Set(System.Xml.XmlElement node, string name, bool withSheets)
        {
            Current = new SubsetClipboard(node, name, withSheets);
        }

        public System.Xml.XmlElement Node { get; }
        public string Name { get; }
        public bool WithSheets { get; }
        public int SubsetCount { get; }
        public int SheetCount { get; }

        public string Summary
        {
            get
            {
                return WithSheets
                    ? $"{SubsetCount} subset, {SheetCount} sheet"
                    : $"{SubsetCount} subset, không kèm sheet";
            }
        }
    }

    /// <summary>Hộp thoại nhập một dòng chữ.</summary>
    internal static class InputDialog
    {
        /// <summary>Trả null nếu bấm Cancel.</summary>
        public static string Show(IWin32Window owner, string title, string prompt, string defaultValue)
        {
            using (var form = new Form())
            using (var label = new Label())
            using (var textBox = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(440, 150);

                label.Text = prompt;
                label.SetBounds(12, 10, 416, 72);

                textBox.Text = defaultValue ?? "";
                textBox.SetBounds(12, 84, 416, 23);
                textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(256, 116, 82, 26);

                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(346, 116, 82, 26);

                form.Controls.AddRange(new Control[] { label, textBox, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.Shown += (s, e) => { textBox.Focus(); textBox.SelectAll(); };

                return form.ShowDialog(owner) == DialogResult.OK ? textBox.Text : null;
            }
        }
    }

    #endregion

    #region PropertyGrid adapter

    /// <summary>Cầu nối giữa PropertyGrid và các giá trị chờ Apply trong viewer.</summary>
    internal interface IPropertyEditSession
    {
        string GetValue(SsmProperty prop);
        bool HasPending(SsmProperty prop);
        bool SetPending(SsmComponent comp, SsmProperty prop, string value);
    }

    /// <summary>
    /// Cho PropertyGrid hiển thị danh sách thuộc tính động, nhóm theo Category.
    /// Tên descriptor ổn định (nhóm + propname) để khi chọn nhiều sheet, PropertyGrid
    /// gộp được các thuộc tính chung; ô có giá trị khác nhau sẽ để trống.
    /// </summary>
    internal sealed class PropertyBag : ICustomTypeDescriptor
    {
        private readonly PropertyDescriptorCollection properties;
        private readonly string className;

        public PropertyBag(SsmComponent comp, IPropertyEditSession session)
        {
            className = comp.Kind.ToString();

            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            var list = new List<PropertyDescriptor>();

            foreach (var p in comp.Properties)
            {
                string key = (p.IsCustom ? "c|" : "s|") + p.Category + "|" + (p.PropName ?? p.Name);
                int n;
                used.TryGetValue(key, out n);
                used[key] = n + 1;
                if (n > 0) key += "#" + n;

                list.Add(new BagPropertyDescriptor(key, comp, p, session));
            }

            properties = new PropertyDescriptorCollection(list.ToArray(), true);
        }

        public AttributeCollection GetAttributes() { return AttributeCollection.Empty; }
        public string GetClassName() { return className; }
        public string GetComponentName() { return null; }
        public TypeConverter GetConverter() { return null; }
        public EventDescriptor GetDefaultEvent() { return null; }
        public PropertyDescriptor GetDefaultProperty() { return null; }
        public object GetEditor(Type editorBaseType) { return null; }
        public EventDescriptorCollection GetEvents() { return EventDescriptorCollection.Empty; }
        public EventDescriptorCollection GetEvents(Attribute[] attributes) { return EventDescriptorCollection.Empty; }
        public PropertyDescriptorCollection GetProperties() { return properties; }
        public PropertyDescriptorCollection GetProperties(Attribute[] attributes) { return properties; }
        public object GetPropertyOwner(PropertyDescriptor pd) { return this; }
    }

    internal sealed class BagPropertyDescriptor : PropertyDescriptor
    {
        private readonly SsmComponent comp;
        private readonly SsmProperty prop;
        private readonly IPropertyEditSession session;

        public BagPropertyDescriptor(string key, SsmComponent comp, SsmProperty prop, IPropertyEditSession session)
            : base(key, new Attribute[]
            {
                // PropertyGrid xếp nhóm theo alphabet; thêm '\t' ở đầu để nhóm quan trọng lên trước
                // (ký tự tab không hiện ra trên giao diện).
                new CategoryAttribute(new string('\t', prop.CategoryPriority) + prop.Category),
                new DescriptionAttribute(prop.Editable
                    ? prop.Description + "  (có thể sửa — chữ đậm = đang chờ Apply)"
                    : prop.Description),
                new ReadOnlyAttribute(!prop.Editable)
            })
        {
            this.comp = comp;
            this.prop = prop;
            this.session = session;
        }

        public override string DisplayName { get { return prop.Name; } }
        public override Type ComponentType { get { return typeof(PropertyBag); } }
        public override bool IsReadOnly { get { return !prop.Editable || session == null; } }
        public override Type PropertyType { get { return typeof(string); } }
        public override bool CanResetValue(object component) { return false; }
        public override object GetValue(object component) { return session != null ? session.GetValue(prop) : prop.Value; }
        public override void ResetValue(object component) { }

        public override void SetValue(object component, object value)
        {
            if (IsReadOnly) return;
            if (session.SetPending(comp, prop, value as string ?? ""))
                OnValueChanged(component, EventArgs.Empty);
        }

        /// <summary>PropertyGrid hiện chữ đậm cho giá trị "đã đổi" → dùng để đánh dấu giá trị chờ Apply.</summary>
        public override bool ShouldSerializeValue(object component)
        {
            return session != null && session.HasPending(prop);
        }
    }

    #endregion
}
