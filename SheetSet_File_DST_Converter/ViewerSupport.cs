using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SheetSet_File_DST_Converter.Helper;

namespace SheetSet_File_DST_Converter
{
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
