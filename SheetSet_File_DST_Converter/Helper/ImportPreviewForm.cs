using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SheetSet_File_DST_Converter.Helper;

namespace SheetSet_File_DST_Converter
{
    /// <summary>
    /// Xem trước các thay đổi sắp import từ Excel (giá trị cũ → mới).
    /// Bỏ tích dòng nào thì dòng đó không được áp dụng. Phần cảnh báo liệt kê các dòng / ô bị bỏ qua.
    /// </summary>
    internal sealed class ImportPreviewForm : Form
    {
        private readonly ListView list;
        private readonly Button btnApply;
        private bool populating;

        /// <summary>Các thay đổi còn được tích khi bấm Áp dụng.</summary>
        public List<ImportChange> SelectedChanges { get; private set; } = new List<ImportChange>();

        public ImportPreviewForm(string summary, List<ImportChange> changes, List<string> warnings)
        {
            Text = "Import từ Excel — xem trước thay đổi";
            Size = new Size(1000, 640);
            MinimumSize = new Size(720, 420);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var lblSummary = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(8, 8, 8, 0),
                Text = summary
            };

            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                CheckBoxes = true,
                HideSelection = false
            };
            list.Columns.Add("Sheet", 260);
            list.Columns.Add("Thuộc tính", 150);
            list.Columns.Add("Giá trị cũ", 260);
            list.Columns.Add("Giá trị mới", 260);
            list.ItemChecked += (s, e) => { if (!populating) UpdateApplyButton(); };

            populating = true;
            list.BeginUpdate();
            foreach (var ch in changes)
            {
                list.Items.Add(new ListViewItem(new[]
                {
                    ch.Component.DisplayText, ch.Property.Name, ch.OldValue, ch.NewValue
                })
                {
                    Checked = true,
                    Tag = ch
                });
            }
            list.EndUpdate();
            populating = false;

            // ===== Nút bấm
            btnApply = new Button { Text = "Áp dụng", Width = 130, Height = 28 };
            btnApply.Font = new Font(btnApply.Font, FontStyle.Bold);
            btnApply.Click += (s, e) =>
            {
                SelectedChanges = list.CheckedItems.Cast<ListViewItem>().Select(i => (ImportChange)i.Tag).ToList();
                DialogResult = DialogResult.OK;
            };

            var btnCancel = new Button { Text = "Huỷ", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            var btnAll = new Button { Text = "Chọn hết", Width = 90, Height = 28 };
            var btnNone = new Button { Text = "Bỏ chọn hết", Width = 100, Height = 28 };
            btnAll.Click += (s, e) => SetAllChecked(true);
            btnNone.Click += (s, e) => SetAllChecked(false);

            var buttonBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 40,
                Padding = new Padding(6, 6, 6, 4)
            };
            buttonBar.Controls.Add(btnCancel);
            buttonBar.Controls.Add(btnApply);
            buttonBar.Controls.Add(btnNone);
            buttonBar.Controls.Add(btnAll);

            AcceptButton = btnApply;
            CancelButton = btnCancel;

            // Thứ tự Add: Fill trước, các thanh Bottom (cảnh báo rồi nút bấm), Top cuối
            Controls.Add(list);

            if (warnings != null && warnings.Count > 0)
            {
                var txtWarnings = new TextBox
                {
                    Dock = DockStyle.Bottom,
                    Height = 120,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = SystemColors.Window,
                    Text = $"Cảnh báo — {warnings.Count} mục bị bỏ qua:" + Environment.NewLine +
                           string.Join(Environment.NewLine, warnings)
                };
                Controls.Add(txtWarnings);
            }

            Controls.Add(buttonBar);
            Controls.Add(lblSummary);

            UpdateApplyButton();
        }

        private void SetAllChecked(bool value)
        {
            populating = true;
            list.BeginUpdate();
            foreach (ListViewItem item in list.Items) item.Checked = value;
            list.EndUpdate();
            populating = false;
            UpdateApplyButton();
        }

        private void UpdateApplyButton()
        {
            int n = list.CheckedItems.Count;
            btnApply.Text = "Áp dụng (" + n + ")";
            btnApply.Enabled = n > 0;
        }
    }
}
