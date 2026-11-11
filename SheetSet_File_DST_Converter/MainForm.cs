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
    /// Cửa sổ chính: đọc / sửa file Sheet Set (.dst hoặc .xml) giống Sheet Set Manager.
    /// - Menu: Tệp (mở / build DST / Excel), Chỉnh sửa, Xem, Công cụ (chuyển đổi, dictionary), Trợ giúp.
    /// - Trái: cây Sheet Set / Subset / Sheet, chọn nhiều bằng Ctrl / Shift + Click.
    /// - Phải: bảng thuộc tính theo nhóm. Chọn nhiều sheet → sửa giá trị chung cho tất cả.
    /// - Giá trị nhập là "chờ ghi" (chữ đậm) cho tới khi bấm Apply; "Build DST" để lưu file.
    /// - Copy / Paste subset, Xoá subset / sheet, Hoàn tác (Ctrl+Z) cho Xoá / Paste / Apply.
    /// - Kéo thả file .dst / .xml vào cửa sổ để mở.
    /// Form tạo hoàn toàn bằng code (không cần file Designer/resx).
    /// </summary>
    public sealed class MainForm : Form, IPropertyEditSession
    {
        private const int IconSheetSet = 0;
        private const int IconSubset = 1;
        private const int IconSheet = 2;
        private const int IconSheetMissing = 3;

        private const string AppTitle = "SheetSet File .DST Converter - Dev by Ba'ch Nguyen";

        private SheetSetDocument document;     // null khi chưa mở file
        private SsmComponent Root { get { return document?.Root; } } // dựng lại sau khi paste
        private DstXmlConverter converter;
        private string dictionaryPath;         // null = dùng bảng built-in
        private string sourcePath;             // file đang mở (.dst hoặc .xml)
        private string sourceDstPath;          // null nếu mở từ file XML
        private string sourceDstHash;          // nội dung file .dst lúc mở → phát hiện SSM sửa file trong lúc đang mở
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
        private readonly ToolStripStatusLabel lblDictionary;
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
        private readonly ToolStripMenuItem miExportSelected;
        private readonly ToolStripMenuItem miOpenExcelSelected;
        private string lastExcelPath; // file Excel tạm / gần nhất đã mở hoặc import
        private const string ExcelFilter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*";
        private readonly System.Windows.Forms.Timer filterTimer;
        private Font rootFont;

        public MainForm()
        {
            converter = DstXmlConverter.Create(null); // bảng built-in

            Text = AppTitle;
            Size = new Size(1150, 740);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            DragEnter += MainForm_DragEnter;
            DragDrop += MainForm_DragDrop;

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
            miExportSelected = new ToolStripMenuItem("Xuất Excel các mục đang chọn...", null, (s, e) => ExportExcel(true));
            miOpenExcelSelected = new ToolStripMenuItem("Mở các mục đang chọn trong Excel", null, (s, e) => OpenInExcel(true));
            miOpenDwg.Font = new Font(miOpenDwg.Font, FontStyle.Bold); // hành động mặc định khi double-click

            menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[]
            {
                miOpenDwg, miOpenFolder, miSelectSheets, new ToolStripSeparator(),
                miCopySubset, miCopySubsetWithSheets, miPasteSubset, new ToolStripSeparator(),
                miDelete, miDeleteSheetsInside, new ToolStripSeparator(),
                miCopyName, miCopyPath, new ToolStripSeparator(),
                miExportSelected, miOpenExcelSelected
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

            toolStrip.Items.Add(new ToolStripButton("Mở .DST...", null, (s, e) => OpenWithDialog(true))
            {
                ToolTipText = "Mở file sheet set .dst (Ctrl+O) — hoặc kéo thả file vào cửa sổ"
            });
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
            lblDictionary = new ToolStripStatusLabel { BorderSides = ToolStripStatusLabelBorderSides.Left };
            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(lblStatus);
            statusStrip.Items.Add(lblDictionary);

            // ===== Menu chính
            var menuStrip = BuildMenu();
            MainMenuStrip = menuStrip;

            // Thứ tự Add quan trọng: control Fill phải add trước để các thanh Top/Bottom dock trước
            Controls.Add(split);
            Controls.Add(toolStrip);
            Controls.Add(menuStrip);   // add sau toolStrip → nằm trên cùng
            Controls.Add(statusStrip);
            UpdateDictionaryLabel();

            filterTimer = new System.Windows.Forms.Timer { Interval = 300 };
            filterTimer.Tick += (s, e) => { filterTimer.Stop(); BuildTree(txtFilter.Text.Trim()); };

            Load += (s, e) =>
            {
                split.SplitterDistance = Math.Min(400, Math.Max(150, ClientSize.Width / 3));
                foreach (Control c in Controls) EnableDropRecursive(c);
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
            if (e.CloseReason == CloseReason.UserClosing && !ConfirmDiscardChanges("đóng chương trình"))
                e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.O))
            {
                OpenWithDialog(true);
                return true;
            }
            if (keyData == Keys.F5)
            {
                ReloadFile();
                return true;
            }
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

        private void ShowEmptyTree()
        {
            tree.BeginUpdate();
            try
            {
                selection.Clear();
                anchorNode = null;
                SetNativeSelected(null);
                tree.Nodes.Clear();
                tree.Nodes.Add(new TreeNode("(Chưa mở file — Tệp → Mở file .DST, hoặc kéo thả file .dst vào đây)")
                {
                    ForeColor = SystemColors.GrayText
                });
            }
            finally
            {
                tree.EndUpdate();
            }

            UpdateGrid();
            UpdateStatus();
        }

        private void BuildTree(string filter)
        {
            if (Root == null)
            {
                ShowEmptyTree();
                return;
            }

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
            if (suppressAfterSelect || e.Node == null || Root == null) return;
            SelectSingle(e.Node); // click thường hoặc phím mũi tên
        }

        private void Tree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node == null || Root == null) return;

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
            UpdateTitle();

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
                Caption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

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
                UpdateTitle();
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
            UpdateTitle();
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
            if (document == null)
            {
                ShowInfo("Chưa mở file nào. Hãy mở một file .dst (hoặc .xml) trước.");
                return false;
            }
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

                // File .dst đích giờ là bản mình vừa ghi → lần Build sau mặc định ghi vào đó và không cảnh báo nhầm.
                // (Nếu đang mở từ XML thì file .dst vừa build trở thành đích mặc định.)
                if (sourceDstPath == null || DstFileGuard.SamePath(dstPath, sourceDstPath))
                {
                    sourceDstPath = dstPath;
                    sourceDstHash = DstFileGuard.ComputeHash(dst);
                }

                dirty = false;
                UpdateTitle();
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
                    "kể từ lúc mở file.\n\n" +
                    "Build sẽ GHI ĐÈ các thay đổi đó (file hiện tại vẫn được backup .bak).\n\n" +
                    "Muốn giữ thay đổi của SSM: chọn No, rồi mở lại file (Tệp → Mở lại, F5) và làm lại thao tác.\n\n" +
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
            if (Root == null)
            {
                lblStatus.Text = "Chưa mở file — Tệp → Mở file .DST (Ctrl+O) hoặc kéo thả file vào cửa sổ" +
                                 (string.IsNullOrEmpty(statusNote) ? "" : "  •  " + statusNote);
                return;
            }

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

            int selectedSheets = GetSelectedSheetNodes().Count;
            miExportSelected.Text = $"Xuất Excel các mục đang chọn ({selectedSheets} sheet)...";
            miExportSelected.Enabled = selectedSheets > 0;
            miOpenExcelSelected.Text = $"Mở các mục đang chọn trong Excel ({selectedSheets} sheet)";
            miOpenExcelSelected.Enabled = selectedSheets > 0;

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
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Copy tên các mục đang chọn, theo thứ tự trên cây, mỗi dòng một mục.</summary>
        private void CopySelectedNames()
        {
            var ordered = SelectedInTreeOrder().Select(c => c.DisplayText);
            CopyText(string.Join(Environment.NewLine, ordered));
        }

        private static void CopyText(string text)
        {
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }

        #endregion

        #region Mở file

        private const string XmlFilter = "XML files (*.xml)|*.xml|All files (*.*)|*.*";
        private const string DstFilter = "DST files (*.dst)|*.dst|All files (*.*)|*.*";
        private const string JsonFilter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        private const string Caption = "SheetSet DST Converter";

        private void OpenWithDialog(bool dst)
        {
            string path = dst
                ? AskOpenPath(DstFilter, "Mở file Sheet Set (.dst)", sourcePath)
                : AskOpenPath(XmlFilter, "Mở file XML Sheet Set", sourcePath);

            if (path != null) OpenFile(path);
        }

        private void ReloadFile()
        {
            if (sourcePath == null) return;
            OpenFile(sourcePath);
        }

        /// <summary>
        /// Mở file .dst (giải mã trong bộ nhớ, không cần tạo XML) hoặc file .xml.
        /// Lỗi → giữ nguyên file đang mở. Trả true nếu đã mở.
        /// </summary>
        private bool OpenFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(this, "Không tìm thấy file:\n" + path, Caption,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string ext = Path.GetExtension(path);
            bool isDst = string.Equals(ext, ".dst", StringComparison.OrdinalIgnoreCase);
            bool isXml = string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase);
            if (!isDst && !isXml)
            {
                MessageBox.Show(this, "Chỉ mở được file .dst hoặc .xml.", Caption,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!ConfirmDiscardChanges("mở file khác")) return false;

            Cursor = Cursors.WaitCursor;
            try
            {
                byte[] xmlBytes;
                string dstPath = null;
                string dstHash = null;

                if (isDst)
                {
                    byte[] dstBytes = File.ReadAllBytes(path);
                    xmlBytes = converter.DstToXml(dstBytes);
                    dstPath = path;
                    dstHash = DstFileGuard.ComputeHash(dstBytes);
                }
                else
                {
                    xmlBytes = File.ReadAllBytes(path);
                }

                // Đường dẫn tương đối của DWG tính từ thư mục chứa file nguồn
                var doc = SheetSetDocument.Load(xmlBytes, Path.GetDirectoryName(path));

                // Đọc thành công mới đổi trạng thái
                document = doc;
                sourcePath = path;
                sourceDstPath = dstPath;
                sourceDstHash = dstHash;

                pending.Clear();
                undoStack.Clear();
                dirty = false;
                statusNote = null;

                txtFilter.Text = "";
                filterTimer.Stop();

                UpdateUndoUi();
                UpdatePendingUi();
                BuildTree("");
                UpdateTitle();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Lỗi mở file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// Trước khi mở file khác / đóng chương trình: xử lý giá trị chưa Apply và thay đổi chưa Build DST.
        /// Trả false nếu người dùng chọn Cancel.
        /// </summary>
        private bool ConfirmDiscardChanges(string action)
        {
            if (document == null) return true;
            if (!ResolvePending(action)) return false;
            if (!dirty) return true;

            var answer = MessageBox.Show(this,
                "Có thay đổi chưa được build thành DST.\n\n" +
                "Build DST trước khi " + action + "?\n(No = bỏ các thay đổi)",
                Caption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) return BuildDst();
            return true;
        }

        private void UpdateTitle()
        {
            if (document == null)
            {
                Text = AppTitle;
                return;
            }

            string mark = dirty ? "  *" : "";
            Text = Root.DisplayText + "   [" + Path.GetFileName(sourcePath) + "]" + mark + " - " + Caption;
        }

        private void UpdateDictionaryLabel()
        {
            string name = dictionaryPath == null ? "built-in" : Path.GetFileName(dictionaryPath);
            lblDictionary.Text = "Dictionary: " + name + " (" + converter.KnownByteCount + "/256 byte)";
            lblDictionary.ToolTipText = dictionaryPath ?? "Bảng mã dựng sẵn trong chương trình";
        }

        /// <summary>Control con nằm đè lên form nên cũng phải nhận kéo thả (kể cả control bên trong PropertyGrid).</summary>
        private void EnableDropRecursive(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += MainForm_DragEnter;
            control.DragDrop += MainForm_DragDrop;
            foreach (Control child in control.Controls) EnableDropRecursive(child);
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            // Mở sau khi kết thúc thao tác kéo thả, để hộp thoại không chặn Explorer
            string path = files[0];
            BeginInvoke(new Action(() => OpenFile(path)));
        }

        #endregion

        #region Menu chính

        private static ToolStripMenuItem Item(string text, string shortcut, Action action)
        {
            var item = new ToolStripMenuItem(text);
            if (!string.IsNullOrEmpty(shortcut)) item.ShortcutKeyDisplayString = shortcut;
            if (action != null) item.Click += (s, e) => action();
            return item;
        }

        private MenuStrip BuildMenu()
        {
            // ===== Tệp
            var miOpenDst = Item("Mở file .DST...", "Ctrl+O", () => OpenWithDialog(true));
            var miOpenXml = Item("Mở file .XML...", null, () => OpenWithDialog(false));
            var miReload = Item("Mở lại file", "F5", ReloadFile);
            var miBuild = Item("Build DST (lưu thành .DST)...", "Ctrl+S", () => BuildDst());
            var miXlOpenAll = Item("Mở trong Excel — toàn bộ sheet", null, () => OpenInExcel(false));
            var miXlOpenSel = Item("Mở trong Excel — các mục đang chọn", null, () => OpenInExcel(true));
            var miXlImportLast = Item("Import lại từ file Excel gần nhất", null, ImportFromLastExcel);
            var miXlImportFile = Item("Import từ file Excel...", null, ImportFromExcelDialog);
            var miExcelAll = Item("Lưu thành file .xlsx — toàn bộ sheet...", null, () => ExportExcel(false));
            var miExcelSel = Item("Lưu thành file .xlsx — các mục đang chọn...", null, () => ExportExcel(true));
            var mExcel = new ToolStripMenuItem("Excel");
            mExcel.DropDownItems.AddRange(new ToolStripItem[]
            {
                miXlOpenAll, miXlOpenSel, new ToolStripSeparator(),
                miXlImportLast, miXlImportFile, new ToolStripSeparator(),
                miExcelAll, miExcelSel
            });
            var miExit = Item("Thoát", "Alt+F4", () => Close());

            var mFile = new ToolStripMenuItem("&Tệp");
            mFile.DropDownItems.AddRange(new ToolStripItem[]
            {
                miOpenDst, miOpenXml, miReload, new ToolStripSeparator(),
                miBuild, new ToolStripSeparator(), mExcel, new ToolStripSeparator(), miExit
            });
            mFile.DropDownOpening += (s, e) =>
            {
                bool has = document != null;
                miReload.Enabled = sourcePath != null;
                miBuild.Enabled = has;
                miExcelAll.Enabled = has;
                miXlOpenAll.Enabled = has;
                miXlImportFile.Enabled = has;

                int selectedSheets = has ? GetSelectedSheetNodes().Count : 0;
                miExcelSel.Text = $"Lưu thành file .xlsx — các mục đang chọn ({selectedSheets} sheet)...";
                miExcelSel.Enabled = selectedSheets > 0;
                miXlOpenSel.Text = $"Mở trong Excel — các mục đang chọn ({selectedSheets} sheet)";
                miXlOpenSel.Enabled = selectedSheets > 0;

                bool hasLast = has && lastExcelPath != null && File.Exists(lastExcelPath);
                miXlImportLast.Text = hasLast
                    ? "Import lại từ file Excel gần nhất (" + Path.GetFileName(lastExcelPath) + ")"
                    : "Import lại từ file Excel gần nhất";
                miXlImportLast.Enabled = hasLast;
            };

            // ===== Chỉnh sửa
            var miUndo = Item("Hoàn tác", "Ctrl+Z", Undo);
            var miCopySub = Item("Copy subset (chỉ subset)", null, () => CopySubset(false));
            var miCopySubSheets = Item("Copy subset kèm sheet", "Ctrl+C", () => CopySubset(true));
            var miPaste = Item("Paste subset vào mục đang chọn", "Ctrl+V", PasteSubset);
            var miDel = Item("Xoá các mục đang chọn", "Del", DeleteSelected);
            var miDelSheets = Item("Xoá tất cả sheet bên trong (giữ subset)", null, DeleteSheetsInside);
            var miSelAll = Item("Chọn tất cả sheet", "Ctrl+A", SelectAllVisibleSheets);
            var miFind = Item("Tìm / lọc...", "Ctrl+F", () => { txtFilter.Focus(); txtFilter.SelectAll(); });
            var miSaveSel = Item("Lưu danh sách đang chọn ra file...", null, SaveSelectionList);
            var miLoadSel = Item("Nạp danh sách chọn từ file...", null, LoadSelectionList);

            var mEdit = new ToolStripMenuItem("&Chỉnh sửa");
            mEdit.DropDownItems.AddRange(new ToolStripItem[]
            {
                miUndo, new ToolStripSeparator(),
                miCopySub, miCopySubSheets, miPaste, new ToolStripSeparator(),
                miDel, miDelSheets, new ToolStripSeparator(),
                miSelAll, miFind, new ToolStripSeparator(),
                miSaveSel, miLoadSel
            });
            mEdit.DropDownOpening += (s, e) =>
            {
                bool has = document != null;
                var comp = PrimaryComponent;
                bool isSubset = comp != null && comp.Kind == SsmNodeKind.Subset;
                bool isContainer = comp != null && comp.Kind != SsmNodeKind.Sheet;

                miUndo.Enabled = undoStack.Count > 0;
                miUndo.Text = undoStack.Count > 0
                    ? "Hoàn tác: " + undoStack[undoStack.Count - 1].Label
                    : "Hoàn tác";

                miCopySub.Enabled = isSubset;
                miCopySubSheets.Enabled = isSubset;
                miPaste.Enabled = isContainer && SubsetClipboard.Current != null;
                miDel.Enabled = SelectedComponents.Any(c => c.Kind != SsmNodeKind.SheetSet);
                miDelSheets.Enabled = isContainer && comp.CountSheets() > 0;
                miSelAll.Enabled = has;
                miFind.Enabled = has;
                miSaveSel.Enabled = has && SelectedComponents.Any();
                miLoadSel.Enabled = has;
            };

            // ===== Xem
            var miExpand = Item("Mở rộng tất cả", null, ExpandAll);
            var miCollapse = Item("Thu gọn", null, CollapseToSubsets);
            var miXmlTree = Item("Xem cấu trúc XML (TreeView)...", null, ShowXmlTree);

            var mView = new ToolStripMenuItem("&Xem");
            mView.DropDownItems.AddRange(new ToolStripItem[]
            {
                miExpand, miCollapse, new ToolStripSeparator(), miXmlTree
            });
            mView.DropDownOpening += (s, e) =>
            {
                bool has = document != null;
                miExpand.Enabled = has;
                miCollapse.Enabled = has;
                miXmlTree.Enabled = has;
            };

            // ===== Công cụ
            var miDstToXml = Item("Chuyển DST → XML...", null, ToolsDstToXml);
            var miXmlToDst = Item("Chuyển XML → DST...", null, ToolsXmlToDst);
            var miGenDict = Item("Tạo dictionary từ cặp file DST + XML mẫu...", null, ToolsGenerateDictionary);
            var miChooseDict = Item("Chọn file dictionary (.json)...", null, ToolsChooseDictionary);
            var miBuiltIn = Item("Dùng dictionary built-in", null, ToolsUseBuiltInDictionary);

            var mTools = new ToolStripMenuItem("Cô&ng cụ");
            mTools.DropDownItems.AddRange(new ToolStripItem[]
            {
                miDstToXml, miXmlToDst, new ToolStripSeparator(),
                miGenDict, miChooseDict, miBuiltIn
            });
            mTools.DropDownOpening += (s, e) =>
            {
                miBuiltIn.Checked = dictionaryPath == null;
                miChooseDict.Checked = dictionaryPath != null;
                miChooseDict.ToolTipText = dictionaryPath;
            };

            // ===== Trợ giúp
            var mHelp = new ToolStripMenuItem("T&rợ giúp");
            mHelp.DropDownItems.Add(Item("Hướng dẫn && phím tắt...", null, ShowHelp));

            var menuStrip = new MenuStrip();
            menuStrip.Items.AddRange(new ToolStripItem[] { mFile, mEdit, mView, mTools, mHelp });
            return menuStrip;
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "Mở file: Tệp → Mở file .DST (Ctrl+O), hoặc kéo thả file .dst / .xml vào cửa sổ.\n\n" +
                "Chọn nhiều: Ctrl+Click, Shift+Click, Ctrl+A (tất cả sheet).\n" +
                "Sửa giá trị: nhập trong bảng thuộc tính bên phải, rồi bấm Apply.\n" +
                "Copy / Paste subset: chuột phải vào subset, hoặc Ctrl+C / Ctrl+V khi đang ở cây.\n" +
                "Xoá: phím Del. Hoàn tác: Ctrl+Z.\n" +
                "Excel: Tệp → Excel → Mở trong Excel (không cần lưu trước), sửa, nhấn Ctrl+S trong Excel,\n" +
                "rồi Import lại (hoặc chọn file khác). Ghép theo cột Sheet ID; chỉ cập nhật sheet có sẵn.\n" +
                "Lưu: Tệp → Build DST (Ctrl+S) — luôn tạo backup .bak.\n\n" +
                "Lưu ý: hãy đóng sheet set trong Sheet Set Manager của AutoCAD trước khi Build DST.",
                "Hướng dẫn", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowXmlTree()
        {
            if (document == null)
            {
                ShowInfo("Hãy mở một file sheet set trước.");
                return;
            }
            if (!ResolvePending("xem cấu trúc XML")) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                new XmlTreeForm(document.Xml, Path.GetFileName(sourcePath)).Show(this);
            }
            catch (Exception ex)
            {
                ShowError("Lỗi xem cấu trúc XML", ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        #endregion

        #region Danh sách chọn

        private const string SelectionFilter = "Selection list (*.json)|*.json|All files (*.*)|*.*";

        /// <summary>Các mục đang chọn, theo thứ tự trên cây (kể cả mục trong nhánh đang thu gọn).</summary>
        private List<SsmComponent> SelectedInTreeOrder()
        {
            var result = new List<SsmComponent>();
            if (tree.Nodes.Count == 0) return result;

            var rootNode = tree.Nodes[0];
            foreach (var node in new[] { rootNode }.Concat(Descendants(rootNode)))
            {
                var comp = node.Tag as SsmComponent;
                if (comp != null && selection.Contains(node)) result.Add(comp);
            }
            return result;
        }

        /// <summary>
        /// Các sheet (phần tử XML) của những mục đang chọn: sheet được chọn,
        /// cộng toàn bộ sheet nằm trong subset / sheet set được chọn. Theo thứ tự trong sheet set, không trùng.
        /// </summary>
        private List<System.Xml.XmlNode> GetSelectedSheetNodes()
        {
            var result = new List<System.Xml.XmlNode>();
            if (document == null) return result;

            var wanted = new HashSet<System.Xml.XmlNode>();
            foreach (var comp in SelectedComponents)
            {
                if (comp.Node == null) continue;

                if (comp.Kind == SsmNodeKind.Sheet)
                {
                    wanted.Add(comp.Node);
                }
                else
                {
                    foreach (System.Xml.XmlNode n in comp.Node.SelectNodes(".//AcSmSheet"))
                        wanted.Add(n);
                }
            }

            foreach (System.Xml.XmlNode n in document.Xml.SelectNodes("//AcSmSheet"))
            {
                if (wanted.Contains(n)) result.Add(n);
            }
            return result;
        }

        private void SaveSelectionList()
        {
            if (document == null)
            {
                ShowInfo("Hãy mở một file sheet set trước.");
                return;
            }

            var items = SelectedInTreeOrder();
            if (items.Count == 0)
            {
                ShowInfo("Chưa chọn mục nào.");
                return;
            }

            try
            {
                string path = AskSavePath("Lưu danh sách đang chọn", SelectionFilter,
                    Path.GetFileNameWithoutExtension(sourcePath) + "_selection.json",
                    Path.GetDirectoryName(sourcePath));
                if (path == null) return;

                SelectionList.Save(path, Root.DisplayText, Path.GetFileName(sourcePath), items);

                int sheets = items.Count(c => c.Kind == SsmNodeKind.Sheet);
                int containers = items.Count - sheets;
                ShowInfo($"Đã lưu danh sách chọn:\n{path}\n\n" +
                         $"{sheets} sheet, {containers} subset / sheet set.\n" +
                         "Nạp lại bằng: Chỉnh sửa → Nạp danh sách chọn từ file.");
            }
            catch (Exception ex)
            {
                ShowError("Lỗi lưu danh sách chọn", ex.Message);
            }
        }

        private void LoadSelectionList()
        {
            if (document == null)
            {
                ShowInfo("Hãy mở một file sheet set trước.");
                return;
            }

            string path = AskOpenPath(SelectionFilter, "Nạp danh sách chọn", sourcePath);
            if (path == null) return;

            try
            {
                var entries = SelectionList.Load(path);
                if (entries.Count == 0)
                {
                    ShowInfo("File danh sách chọn không có mục nào.");
                    return;
                }

                List<SelectionEntry> notFound;
                var comps = SelectionList.Resolve(Root, entries, out notFound);
                if (comps.Count == 0)
                {
                    ShowInfo($"Không tìm thấy mục nào trong {entries.Count} mục của danh sách " +
                             "trong sheet set đang mở.\n\nCó thể danh sách được lưu từ một sheet set khác.");
                    return;
                }

                // Bỏ bộ lọc để thấy đủ các mục sẽ chọn
                if (txtFilter.Text.Length > 0)
                {
                    txtFilter.Text = "";
                    filterTimer.Stop();
                    BuildTree("");
                }

                var nodes = new List<TreeNode>();
                foreach (var comp in comps)
                {
                    var node = FindNode(tree.Nodes[0], comp);
                    if (node != null) nodes.Add(node);
                }
                if (nodes.Count == 0) return;

                // Mở các nhánh chứa mục được chọn
                tree.BeginUpdate();
                try
                {
                    foreach (var n in nodes)
                        for (var p = n.Parent; p != null; p = p.Parent) p.Expand();
                }
                finally
                {
                    tree.EndUpdate();
                }

                SelectNodes(nodes);
                nodes[0].EnsureVisible();

                if (notFound.Count == 0)
                {
                    statusNote = $"Đã nạp danh sách: chọn {nodes.Count} mục";
                    UpdateStatus();
                }
                else
                {
                    var msg = new StringBuilder();
                    msg.AppendLine($"Đã chọn {nodes.Count} mục.");
                    msg.AppendLine();
                    msg.AppendLine($"Không tìm thấy {notFound.Count} mục (đã bị xoá / đổi số hiệu, hoặc trùng số hiệu):");
                    foreach (var entry in notFound.Take(10))
                        msg.AppendLine("  • " + entry.Display);
                    if (notFound.Count > 10) msg.AppendLine($"  … và {notFound.Count - 10} mục khác");
                    ShowInfo(msg.ToString());
                }
            }
            catch (Exception ex)
            {
                ShowError("Lỗi nạp danh sách chọn", ex.Message);
            }
        }

        #endregion

        #region Xuất Excel

        /// <summary>
        /// selectedOnly = true → chỉ xuất các sheet đang chọn và các sheet nằm trong subset đang chọn.
        /// </summary>
        private void ExportExcel(bool selectedOnly)
        {
            if (document == null)
            {
                ShowInfo("Hãy mở một file sheet set trước.");
                return;
            }
            if (!ResolvePending("xuất Excel")) return;

            try
            {
                List<System.Xml.XmlNode> sheets = null;
                if (selectedOnly)
                {
                    sheets = GetSelectedSheetNodes();
                    if (sheets.Count == 0)
                    {
                        ShowInfo("Các mục đang chọn không có sheet nào.");
                        return;
                    }
                }

                string suggestedName = Path.GetFileNameWithoutExtension(sourcePath) +
                                       (selectedOnly ? "_selected_" : "_") +
                                       DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx";

                string excelPath = AskSavePath("Xuất Excel", "Excel Files (*.xlsx)|*.xlsx",
                    suggestedName, Path.GetDirectoryName(sourcePath));
                if (excelPath == null) return;

                // Xuất từ bản đang mở trong bộ nhớ → gồm cả các thay đổi đã Apply
                int count = SheetSetExcelExporter.Export(document.Xml, excelPath, sheets);

                string msg = $"Đã xuất {count} sheet sang Excel:\n{excelPath}";
                if (dirty) msg += "\n\nLưu ý: dữ liệu gồm cả các thay đổi chưa Build DST.";
                ShowInfo(msg);
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

        #region Excel (mở trực tiếp / import)

        private static string ExcelTempDir
        {
            get { return Path.Combine(Path.GetTempPath(), "SheetSetDstConverter"); }
        }

        private static void CleanOldTempFiles()
        {
            try
            {
                if (!Directory.Exists(ExcelTempDir)) return;
                foreach (var f in Directory.GetFiles(ExcelTempDir, "*.xlsx"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Xuất ra file tạm rồi mở thẳng trong Excel (không cần chọn nơi lưu).</summary>
        private void OpenInExcel(bool selectedOnly)
        {
            if (document == null)
            {
                ShowInfo("Hãy mở một file sheet set trước.");
                return;
            }
            if (!ResolvePending("mở trong Excel")) return;

            try
            {
                List<System.Xml.XmlNode> sheets = null;
                if (selectedOnly)
                {
                    sheets = GetSelectedSheetNodes();
                    if (sheets.Count == 0)
                    {
                        ShowInfo("Các mục đang chọn không có sheet nào.");
                        return;
                    }
                }

                Directory.CreateDirectory(ExcelTempDir);
                CleanOldTempFiles();

                string baseName = Path.GetFileNameWithoutExtension(sourcePath ?? "SheetSet");
                foreach (char c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
                string path = Path.Combine(ExcelTempDir, baseName + (selectedOnly ? "_selected_" : "_") +
                                           DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");

                int count = SheetSetExcelExporter.Export(document.Xml, path, sheets);
                lastExcelPath = path;

                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ShowError("Không mở được Excel", "Đã tạo file nhưng không mở được:\n" + path + "\n\n" + ex.Message);
                }

                { statusNote = $"Đã mở {count} sheet trong Excel — sửa xong nhấn Ctrl+S trong Excel rồi chọn Tệp → Excel → Import lại"; UpdateStatus(); }
            }
            catch (Exception ex)
            {
                ShowError("Lỗi mở trong Excel", ex.Message);
            }
        }

        private void ImportFromLastExcel()
        {
            if (lastExcelPath == null || !File.Exists(lastExcelPath))
            {
                ShowInfo("Chưa có file Excel nào để import lại. Hãy dùng \"Mở trong Excel\" hoặc \"Import từ file Excel...\".");
                return;
            }
            ImportFromExcel(lastExcelPath);
        }

        private void ImportFromExcelDialog()
        {
            if (document == null)
            {
                ShowInfo("Hãy mở một file sheet set trước.");
                return;
            }

            string path = AskOpenPath(ExcelFilter, "Chọn file Excel để import", lastExcelPath ?? sourcePath);
            if (path == null) return;

            lastExcelPath = path;
            ImportFromExcel(path);
        }

        private void ImportFromExcel(string path)
        {
            if (document == null)
            {
                ShowInfo("Hãy mở một file sheet set trước.");
                return;
            }
            if (!ResolvePending("import Excel")) return;

            ImportResult result;
            Cursor = Cursors.WaitCursor;
            try
            {
                result = SheetSetExcelImporter.Read(path, Root, ValidateValue);
            }
            catch (IOException ex) when (IsFileLocked(ex))
            {
                Cursor = Cursors.Default;
                ShowError("Lỗi import Excel",
                    "Không đọc được file. Hãy nhấn Ctrl+S trong Excel (hoặc đóng file) rồi thử lại.\n\n" + ex.Message);
                return;
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                ShowError("Lỗi import Excel", ex.Message);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (result.Changes.Count == 0 && result.Warnings.Count == 0)
            {
                string msg = "Không có giá trị nào khác so với sheet set hiện tại.";
                if (string.Equals(path, lastExcelPath, StringComparison.OrdinalIgnoreCase))
                    msg += "\n\nNếu bạn vừa sửa trong Excel, hãy nhấn Ctrl+S trong Excel rồi Import lại.";
                ShowInfo(msg);
                return;
            }

            int sheetCount = result.Changes.Select(c => c.Component).Distinct().Count();
            string summary = Path.GetFileName(path) + Environment.NewLine +
                             $"Đọc {result.RowsRead} dòng: khớp {result.RowsMatched}, không tìm thấy {result.RowsNotFound}. " +
                             $"{result.Changes.Count} thay đổi trong {sheetCount} sheet.";

            List<ImportChange> chosen;
            using (var dlg = new ImportPreviewForm(summary, result.Changes, result.Warnings))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                chosen = dlg.SelectedChanges;
            }
            if (chosen.Count == 0) return;

            pending.Clear();
            foreach (var ch in chosen)
                pending[ch.Property] = new PendingEdit(ch.Component, ch.Property, ch.NewValue);
            UpdatePendingUi();

            if (!ApplyPending())
            {
                DiscardPending();
                return;
            }

            { statusNote = $"Đã import {chosen.Count} giá trị từ {Path.GetFileName(path)}"; UpdateStatus(); }
        }

        #endregion

        #region Công cụ chuyển đổi

        /// <summary>Chuyển một file .dst bất kỳ sang .xml (độc lập với file đang mở).</summary>
        private void ToolsDstToXml()
        {
            try
            {
                string dstPath = AskOpenPath(DstFilter, "Chọn file .dst cần chuyển sang XML", sourceDstPath);
                if (dstPath == null) return;

                byte[] xml = converter.DstToXml(File.ReadAllBytes(dstPath));

                // Kết quả không phải XML hợp lệ → nhiều khả năng dictionary sai
                string error;
                if (!DstXmlConverter.IsWellFormedXml(xml, out error))
                {
                    var answer = MessageBox.Show(this,
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

                var open = MessageBox.Show(this,
                    "DST → XML đã hoàn tất:\n" + xmlPath + "\n\nMở file XML này trong cửa sổ chính?",
                    "Hoàn tất chuyển đổi", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (open == DialogResult.Yes) OpenFile(xmlPath);
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

        /// <summary>Chuyển một file .xml bất kỳ (vd: tự sửa bằng editor) sang .dst.</summary>
        private void ToolsXmlToDst()
        {
            try
            {
                string hint = sourceDstPath ?? sourcePath;
                string xmlPath = AskOpenPath(XmlFilter, "Chọn file .xml cần chuyển sang DST", hint);
                if (xmlPath == null) return;

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

        /// <summary>
        /// Học thêm byte từ một cặp file mẫu (.dst + bản XML nguyên gốc của nó),
        /// gộp với bảng hiện tại, lưu ra JSON và dùng luôn.
        /// </summary>
        private void ToolsGenerateDictionary()
        {
            try
            {
                string dstPath = AskOpenPath(DstFilter, "Bước 1/2: chọn file .dst mẫu", sourceDstPath);
                if (dstPath == null) return;

                string xmlPath = AskOpenPath(XmlFilter,
                    "Bước 2/2: chọn file .xml mẫu (bản giải mã NGUYÊN GỐC của file .dst vừa chọn)", dstPath);
                if (xmlPath == null) return;

                // Gộp: built-in + dictionary đang dùng + cặp file mẫu
                var learner = DstXmlConverter.Create(dictionaryPath);
                int added = learner.Learn(File.ReadAllBytes(xmlPath), File.ReadAllBytes(dstPath));

                string outJson = AskSavePath("Lưu file dictionary", JsonFilter, "dictionary.json",
                    Path.GetDirectoryName(dstPath));
                if (outJson == null) return;

                learner.SaveDictionary(outJson);
                SetDictionary(outJson);

                ShowInfo($"Đã lưu dictionary:\n{outJson}\n\n" +
                         $"Học thêm {added} byte mới. Tổng cộng đã biết {converter.KnownByteCount}/256 byte.\n" +
                         "Chương trình đang dùng dictionary này.");
            }
            catch (Exception ex)
            {
                ShowError("Lỗi tạo dictionary", ex.Message);
            }
        }

        private void ToolsChooseDictionary()
        {
            string path = AskOpenPath(JsonFilter, "Chọn file dictionary (.json)", dictionaryPath);
            if (path == null) return;

            try
            {
                SetDictionary(path);
                ShowInfo($"Đang dùng dictionary:\n{path}\n\n" +
                         $"Đã biết {converter.KnownByteCount}/256 byte (gồm cả bảng built-in).");
            }
            catch (Exception ex)
            {
                ShowError("Lỗi đọc dictionary", ex.Message);
            }
        }

        private void ToolsUseBuiltInDictionary()
        {
            try
            {
                SetDictionary(null);
            }
            catch (Exception ex)
            {
                ShowError("Lỗi", ex.Message);
            }
        }

        /// <summary>Đổi dictionary (null = built-in). Lỗi → giữ nguyên dictionary cũ.</summary>
        private void SetDictionary(string path)
        {
            var newConverter = DstXmlConverter.Create(path);
            converter = newConverter;
            dictionaryPath = string.IsNullOrWhiteSpace(path) ? null : path;
            UpdateDictionaryLabel();
        }

        #endregion

        #region Helpers

        private string AskOpenPath(string filter, string title, string currentPath)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = filter;
                ofd.Title = title;
                try
                {
                    string dir = string.IsNullOrWhiteSpace(currentPath)
                        ? null
                        : Path.GetDirectoryName(currentPath.Trim());
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        ofd.InitialDirectory = dir;
                }
                catch (ArgumentException) { /* đường dẫn không hợp lệ → bỏ qua */ }

                return ofd.ShowDialog(this) == DialogResult.OK ? ofd.FileName : null;
            }
        }

        private string AskSavePath(string title, string filter, string fileName, string initialDir)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = title;
                sfd.Filter = filter;
                sfd.FileName = fileName;
                if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                    sfd.InitialDirectory = initialDir;

                return sfd.ShowDialog(this) == DialogResult.OK ? sfd.FileName : null;
            }
        }

        /// <summary>ERROR_SHARING_VIOLATION (32) hoặc ERROR_LOCK_VIOLATION (33).</summary>
        private static bool IsFileLocked(IOException ex)
        {
            int code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33;
        }

        private void ShowInfo(string message)
        {
            MessageBox.Show(this, message, "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowError(string title, string message)
        {
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
}
