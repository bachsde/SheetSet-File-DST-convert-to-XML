using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace SheetSet_File_DST_Converter.Helper
{
    public enum SsmNodeKind
    {
        SheetSet,
        Subset,
        Sheet
    }

    /// <summary>Một dòng thuộc tính hiển thị trong Sheet Set Viewer.</summary>
    public sealed class SsmProperty
    {
        public SsmProperty(string category, string name, string value, string description, int categoryPriority)
        {
            Category = category;
            Name = name;
            Value = value ?? "";
            Description = description ?? "";
            CategoryPriority = categoryPriority;
        }

        public string Category { get; }
        public string Name { get; }
        public string Value { get; internal set; }
        public string Description { get; }

        /// <summary>Số lớn hơn → nhóm hiện trước (nhóm chính, custom, thông tin file, còn lại).</summary>
        public int CategoryPriority { get; }

        /// <summary>propname gốc trong XML (Number, Title, Desc, ...) hoặc tên custom property.</summary>
        public string PropName { get; internal set; }

        public bool IsCustom { get; internal set; }

        /// <summary>Phần tử &lt;AcSmProp&gt; chứa giá trị. Chỉ có khi thuộc tính sửa được.</summary>
        internal XmlNode ValueNode { get; set; }

        public bool Editable
        {
            get { return ValueNode != null; }
        }
    }

    /// <summary>Sheet Set / Subset / Sheet.</summary>
    public sealed class SsmComponent
    {
        public SsmNodeKind Kind { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        /// <summary>Đường dẫn DWG dùng để mở (đã thử cả đường dẫn tương đối).</summary>
        public string DwgPath { get; set; }
        public bool DwgExists { get; set; }

        /// <summary>Phần tử XML gốc (AcSmSheetSet / AcSmSubset / AcSmSheet).</summary>
        internal XmlNode Node { get; set; }

        public List<SsmProperty> Properties { get; } = new List<SsmProperty>();
        public List<SsmComponent> Children { get; } = new List<SsmComponent>();

        /// <summary>Giống SSM: sheet hiện "Số - Tên", subset/sheet set hiện tên.</summary>
        public string DisplayText
        {
            get
            {
                if (Kind != SsmNodeKind.Sheet)
                    return string.IsNullOrEmpty(Name) ? "(không tên)" : Name;
                if (string.IsNullOrEmpty(Number)) return Title;
                if (string.IsNullOrEmpty(Title)) return Number;
                return Number + " - " + Title;
            }
        }

        public int CountSheets()
        {
            int n = Kind == SsmNodeKind.Sheet ? 1 : 0;
            foreach (var c in Children) n += c.CountSheets();
            return n;
        }

        public int CountSubsets()
        {
            int n = Kind == SsmNodeKind.Subset ? 1 : 0;
            foreach (var c in Children) n += c.CountSubsets();
            return n;
        }

        public int CountMissingDwg()
        {
            int n = Kind == SsmNodeKind.Sheet && !string.IsNullOrEmpty(DwgPath) && !DwgExists ? 1 : 0;
            foreach (var c in Children) n += c.CountMissingDwg();
            return n;
        }
    }

    /// <summary>Đọc XML Sheet Set thành cây SsmComponent.</summary>
    public static class SheetSetReader
    {
        private const int MaxObjectDepth = 3;

        private const int PriorityMain = 3;
        private const int PriorityCustom = 2;
        private const int PriorityFile = 1;
        private const int PriorityOther = 0;

        // Thuộc tính chuẩn cho phép sửa (giống hộp thoại Properties của SSM)
        private static readonly HashSet<string> EditableSheetProps = new HashSet<string>(StringComparer.Ordinal)
        {
            "Number", "Title", "Desc", "RevisionNumber", "RevisionDate", "Purpose", "Category"
        };
        private static readonly HashSet<string> EditableContainerProps = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name", "Desc"
        };

        private static readonly Dictionary<string, string> FriendlyNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desc"] = "Description",
                ["IncludeForPublish"] = "Include for publish",
                ["RevisionNumber"] = "Revision number",
                ["RevisionDate"] = "Revision date",
                ["FileName"] = "File name",
                ["Relative_FileName"] = "Relative path",
                ["Environ_FileName"] = "Environment path",
                ["Unc_FileName"] = "UNC path",
                ["AcSmAcDbLayoutReference"] = "Layout",
                ["AcSmAcDbViewReference"] = "View",
                ["AcSmAcDbBlockRecordReference"] = "Block",
                ["AcSmFileReference"] = "File",
                ["AcSmSheetViews"] = "Sheet Views",
                ["AcSmSheetView"] = "Sheet View",
            };

        /// <param name="baseFolder">Thư mục chứa file .dst — để tìm DWG theo đường dẫn tương đối.</param>
        public static SsmComponent Read(XmlDocument doc, string baseFolder)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var setNode = doc.SelectSingleNode("//AcSmSheetSet");
            if (setNode == null)
                throw new InvalidDataException("Không tìm thấy AcSmSheetSet trong XML. Đây có phải file Sheet Set không?");

            return ReadComponent(setNode, SsmNodeKind.SheetSet, baseFolder);
        }

        private static SsmComponent ReadComponent(XmlNode node, SsmNodeKind kind, string baseFolder)
        {
            var comp = new SsmComponent
            {
                Node = node,
                Kind = kind,
                Id = Attr(node, "ID"),
                Name = Prop(node, "Name"),
                Number = Prop(node, "Number"),
                Title = Prop(node, "Title"),
                Description = Prop(node, "Desc")
            };

            string mainCategory = kind == SsmNodeKind.SheetSet ? "Sheet Set"
                                : kind == SsmNodeKind.Subset ? "Subset"
                                : "Sheet";

            CollectProperties(node, comp.Properties, mainCategory, PriorityMain, kind, 0);

            if (kind == SsmNodeKind.Sheet)
                ResolveDwg(comp, node, baseFolder);

            if (!string.IsNullOrEmpty(comp.Id))
                comp.Properties.Add(new SsmProperty("Kỹ thuật", "ID", comp.Id, "ID của đối tượng trong file DST", PriorityOther));

            // Con theo đúng thứ tự trong file (giống SSM)
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.Name == "AcSmSubset")
                    comp.Children.Add(ReadComponent(child, SsmNodeKind.Subset, baseFolder));
                else if (child.Name == "AcSmSheet")
                    comp.Children.Add(ReadComponent(child, SsmNodeKind.Sheet, baseFolder));
            }
            return comp;
        }

        /// <summary>
        /// AcSmProp trực tiếp → nhóm hiện tại; custom property → nhóm Custom;
        /// phần tử đối tượng khác (Layout, Sheet Views, ...) → nhóm riêng, đọc đệ quy vài cấp.
        /// </summary>
        private static void CollectProperties(XmlNode node, List<SsmProperty> list, string category,
                                              int priority, SsmNodeKind kind, int depth)
        {
            // Đếm số lần xuất hiện để đánh số các phần tử lặp (Sheet View #1, #2, ...)
            var total = new Dictionary<string, int>();
            foreach (XmlNode el in node.ChildNodes)
            {
                if (el.NodeType != XmlNodeType.Element) continue;
                int n;
                total.TryGetValue(el.Name, out n);
                total[el.Name] = n + 1;
            }

            var index = new Dictionary<string, int>();

            foreach (XmlNode el in node.ChildNodes)
            {
                if (el.NodeType != XmlNodeType.Element) continue;

                switch (el.Name)
                {
                    case "AcSmSubset":
                    case "AcSmSheet":
                        continue; // là node con trong cây, không phải thuộc tính

                    case "AcSmProp":
                        {
                            string pn = Attr(el, "propname");
                            var prop = new SsmProperty(category, Friendly(pn), el.InnerText.Trim(), pn, priority)
                            {
                                PropName = pn
                            };
                            if (depth == 0 && IsEditable(kind, pn))
                                prop.ValueNode = el;
                            list.Add(prop);
                            break;
                        }

                    case "AcSmCustomPropertyBag":
                        foreach (XmlNode v in el.SelectNodes("AcSmCustomPropertyValue"))
                            AddCustom(v, list, kind);
                        break;

                    case "AcSmCustomPropertyValue":
                        AddCustom(el, list, kind);
                        break;

                    default:
                        {
                            if (depth >= MaxObjectDepth) break;

                            string pn = Attr(el, "propname");
                            string label = Friendly(string.IsNullOrEmpty(pn) ? el.Name : pn);

                            if (total[el.Name] > 1)
                            {
                                int i;
                                index.TryGetValue(el.Name, out i);
                                index[el.Name] = ++i;
                                label += " #" + i;
                            }

                            string sub = depth == 0 ? label : category + " / " + label;
                            int subPriority = depth == 0 ? PriorityOther : priority;
                            CollectProperties(el, list, sub, subPriority, kind, depth + 1);
                            break;
                        }
                }
            }
        }

        private static bool IsEditable(SsmNodeKind kind, string propname)
        {
            return kind == SsmNodeKind.Sheet
                ? EditableSheetProps.Contains(propname)
                : EditableContainerProps.Contains(propname);
        }

        /// <summary>
        /// Flags của custom property: bit 1 = thuộc tính Sheet Set, bit 2 = thuộc tính Sheet.
        /// Ở cấp Sheet Set, thuộc tính Sheet là giá trị mặc định cho sheet mới.
        /// </summary>
        private static void AddCustom(XmlNode v, List<SsmProperty> list, SsmNodeKind kind)
        {
            string name = Attr(v, "propname");
            if (string.IsNullOrEmpty(name)) return;

            string category;
            if (kind == SsmNodeKind.SheetSet)
            {
                int flags;
                int.TryParse(Prop(v, "Flags"), out flags);
                category = (flags & 2) != 0 && (flags & 1) == 0
                    ? "Sheet Custom Properties (mặc định)"
                    : "Sheet Set Custom Properties";
            }
            else
            {
                category = "Custom Properties";
            }

            list.Add(new SsmProperty(category, name, Prop(v, "Value"), "Custom property: " + name, PriorityCustom)
            {
                PropName = name,
                IsCustom = true,
                ValueNode = v.SelectSingleNode("AcSmProp[@propname='Value']")
            });
        }

        /// <summary>
        /// Giống SSM: thử đường dẫn tuyệt đối trước, không có thì thử đường dẫn tương đối
        /// tính từ thư mục chứa file .dst.
        /// </summary>
        private static void ResolveDwg(SsmComponent comp, XmlNode sheetNode, string baseFolder)
        {
            var layout = sheetNode.SelectSingleNode("AcSmAcDbLayoutReference");
            if (layout == null) return;

            string full = Prop(layout, "FileName");
            string rel = Prop(layout, "Relative_FileName");

            comp.DwgPath = full;
            comp.DwgExists = SafeFileExists(full);

            if (!comp.DwgExists && !string.IsNullOrEmpty(rel) && !string.IsNullOrEmpty(baseFolder))
            {
                try
                {
                    string candidate = Path.GetFullPath(Path.Combine(baseFolder, rel));
                    if (File.Exists(candidate))
                    {
                        comp.DwgPath = candidate;
                        comp.DwgExists = true;
                    }
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }

            comp.Properties.Add(new SsmProperty("Thông tin file", "DWG tồn tại",
                comp.DwgExists ? "Có" : "KHÔNG TÌM THẤY", "Kiểm tra theo đường dẫn tuyệt đối, sau đó theo đường dẫn tương đối", PriorityFile));
            comp.Properties.Add(new SsmProperty("Thông tin file", "Đường dẫn dùng để mở",
                comp.DwgPath, "Đường dẫn DWG mà viewer sẽ mở khi double-click", PriorityFile));
        }

        private static bool SafeFileExists(string path)
        {
            return !string.IsNullOrEmpty(path) && File.Exists(path); // File.Exists không ném lỗi
        }

        private static string Friendly(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "(không tên)";

            string name;
            if (FriendlyNames.TryGetValue(raw, out name)) return name;

            // AcSmXxxYyy → "Xxx Yyy"; Abc_Def → "Abc Def"
            string s = raw.StartsWith("AcSm", StringComparison.Ordinal) ? raw.Substring(4) : raw;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '_') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(ch) && char.IsLower(s[i - 1])) sb.Append(' ');
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string Prop(XmlNode node, string propname)
        {
            return node.SelectSingleNode($"AcSmProp[@propname='{propname}']")?.InnerText?.Trim() ?? "";
        }

        private static string Attr(XmlNode node, string name)
        {
            return node.Attributes?[name]?.Value?.Trim() ?? "";
        }
    }

    /// <summary>
    /// XML Sheet Set đã nạp, có thể sửa và lưu lại mà giữ nguyên định dạng gốc
    /// (khoảng trắng, xuống dòng CRLF, BOM).
    /// </summary>
    public sealed class SheetSetDocument
    {
        private readonly string baseFolder;

        private SheetSetDocument(XmlDocument xml, SsmComponent root, bool hasBom, bool usesCrLf, string baseFolder)
        {
            Xml = xml;
            Root = root;
            HasBom = hasBom;
            UsesCrLf = usesCrLf;
            this.baseFolder = baseFolder;
        }

        public XmlDocument Xml { get; }

        /// <summary>Cây sheet set. Được dựng lại sau khi paste (các SsmComponent cũ không còn dùng được).</summary>
        public SsmComponent Root { get; private set; }
        public bool HasBom { get; }
        public bool UsesCrLf { get; }

        /// <summary>True nếu lưu lại (chưa sửa gì) cho ra đúng từng byte như file gốc.</summary>
        public bool FormatPreserved { get; private set; }

        public static SheetSetDocument Load(byte[] xmlBytes, string baseFolder)
        {
            if (xmlBytes == null) throw new ArgumentNullException(nameof(xmlBytes));

            if (xmlBytes.Length >= 2 &&
                ((xmlBytes[0] == 0xFF && xmlBytes[1] == 0xFE) || (xmlBytes[0] == 0xFE && xmlBytes[1] == 0xFF)))
                throw new InvalidDataException("File XML đang ở dạng UTF-16. Hãy lưu lại dạng UTF-8 rồi thử lại.");

            bool hasBom = xmlBytes.Length >= 3 && xmlBytes[0] == 0xEF && xmlBytes[1] == 0xBB && xmlBytes[2] == 0xBF;

            bool usesCrLf = false;
            for (int i = 0; i + 1 < xmlBytes.Length; i++)
            {
                if (xmlBytes[i] == 0x0D && xmlBytes[i + 1] == 0x0A) { usesCrLf = true; break; }
            }

            var xml = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using (var ms = new MemoryStream(xmlBytes))
                xml.Load(ms);

            var root = SheetSetReader.Read(xml, baseFolder);
            var document = new SheetSetDocument(xml, root, hasBom, usesCrLf, baseFolder);
            document.FormatPreserved = document.Serialize().SequenceEqual(xmlBytes);
            return document;
        }

        /// <summary>Ghi XML ra byte UTF-8, giữ BOM và kiểu xuống dòng như file gốc.</summary>
        public byte[] Serialize()
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(HasBom),
                Indent = false,
                NewLineHandling = UsesCrLf ? NewLineHandling.Replace : NewLineHandling.None,
                NewLineChars = "\r\n",
                OmitXmlDeclaration = !(Xml.FirstChild is XmlDeclaration)
            };

            using (var ms = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(ms, settings))
                    Xml.Save(writer);
                return ms.ToArray();
            }
        }

        /// <summary>Đọc lại cây từ XML (sau khi thêm / bớt subset, sheet).</summary>
        public void Reload()
        {
            Root = SheetSetReader.Read(Xml, baseFolder);
        }

        #region Copy / Paste subset

        /// <summary>
        /// Tạo bản sao (tách rời) của một subset, gồm các subset con.
        /// includeSheets = false → bỏ toàn bộ sheet, chỉ giữ cấu trúc subset.
        /// </summary>
        public static XmlElement CloneSubset(SsmComponent subset, bool includeSheets)
        {
            if (subset == null) throw new ArgumentNullException(nameof(subset));
            if (subset.Kind != SsmNodeKind.Subset || subset.Node == null)
                throw new InvalidOperationException("Chỉ copy được subset.");

            var clone = (XmlElement)subset.Node.CloneNode(true);

            if (!includeSheets)
            {
                foreach (var sheet in clone.SelectNodes(".//AcSmSheet").Cast<XmlNode>().ToList())
                    RemoveWithIndent(sheet);
            }
            return clone;
        }

        /// <summary>
        /// Paste bản sao subset vào cuối danh sách con của target (sheet set hoặc subset).
        /// Mọi đối tượng trong bản sao được cấp ID mới; tham chiếu nội bộ được cập nhật theo.
        /// Clip có thể đến từ sheet set khác (được import vào tài liệu này).
        /// Trả về phần tử vừa thêm. Gọi xong thì Root đã được dựng lại.
        /// </summary>
        public XmlElement PasteSubset(XmlElement clip, SsmComponent target, string newName)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.Kind == SsmNodeKind.Sheet || target.Node == null)
                throw new InvalidOperationException("Chỉ paste được vào sheet set hoặc subset.");

            var parent = target.Node;
            var node = (XmlElement)Xml.ImportNode(clip, true); // luôn tạo bản mới, kể cả cùng tài liệu

            AssignNewIds(node);

            if (!string.IsNullOrEmpty(newName))
            {
                var nameProp = node.SelectSingleNode("AcSmProp[@propname='Name']");
                if (nameProp != null) nameProp.InnerText = newName;
            }

            // Vị trí: ngay sau subset / sheet con cuối cùng (giống SSM thêm vào cuối)
            XmlNode lastChild = null;
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child.Name == "AcSmSubset" || child.Name == "AcSmSheet")
                    lastChild = child;
            }

            if (lastChild != null)
            {
                parent.InsertAfter(node, lastChild);
                string indent = IndentBefore(lastChild);
                if (indent != null) parent.InsertBefore(Xml.CreateWhitespace(indent), node);
            }
            else
            {
                // Chưa có con: chèn trước khoảng trắng đóng thẻ (nếu có), thụt lề theo phần tử con cuối
                XmlNode trailing = IsWhitespace(parent.LastChild) ? parent.LastChild : null;
                string indent = null;
                for (XmlNode n = parent.LastChild; n != null; n = n.PreviousSibling)
                {
                    if (n.NodeType == XmlNodeType.Element) { indent = IndentBefore(n); break; }
                }

                if (trailing != null) parent.InsertBefore(node, trailing);
                else parent.AppendChild(node);

                if (indent != null) parent.InsertBefore(Xml.CreateWhitespace(indent), node);
            }

            Reload();
            return node;
        }

        /// <summary>Đếm số subset (gồm chính nó) và số sheet trong một bản sao.</summary>
        public static void CountClip(XmlElement clip, out int subsets, out int sheets)
        {
            subsets = 1 + clip.SelectNodes(".//AcSmSubset").Count;
            sheets = clip.SelectNodes(".//AcSmSheet").Count;
        }

        private readonly Random random = new Random();

        private void AssignNewIds(XmlElement root)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            // Mọi ID đang có trong tài liệu → ID mới không được trùng
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode a in Xml.SelectNodes("//@ID"))
                used.Add(a.Value.Trim());

            // 1. Cấp ID mới cho mọi phần tử có thuộc tính ID (kể cả chính root)
            foreach (XmlNode n in root.SelectNodes("descendant-or-self::*[@ID]"))
            {
                var el = (XmlElement)n;
                string oldId = el.GetAttribute("ID").Trim();
                if (oldId.Length == 0) continue;

                string newId;
                if (!map.TryGetValue(oldId, out newId))
                {
                    newId = NewIdLike(oldId, used);
                    map[oldId] = newId;
                }
                el.SetAttribute("ID", newId);
            }

            if (map.Count == 0) return;

            // 2. Cập nhật tham chiếu nội bộ (thuộc tính / text khác có giá trị bằng ID cũ)
            foreach (XmlNode n in root.SelectNodes(".//@* | .//text()"))
            {
                if (n.NodeType == XmlNodeType.Attribute && n.Name == "ID") continue;

                string value = n.Value;
                if (string.IsNullOrEmpty(value)) continue;

                string trimmed = value.Trim();
                string newId;
                if (map.TryGetValue(trimmed, out newId))
                    n.Value = value.Replace(trimmed, newId);
            }
        }

        /// <summary>
        /// Tạo ID mới CÙNG ĐỊNH DẠNG với ID cũ: chỉ đổi ngẫu nhiên các chữ số / ký tự hex,
        /// giữ nguyên mọi ký tự khác và kiểu chữ hoa/thường. Nhờ vậy ID mới chỉ dùng
        /// những ký tự đã có trong file → luôn mã hoá được sang DST.
        /// </summary>
        private string NewIdLike(string oldId, HashSet<string> used)
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string candidate = MutateId(oldId);
                if (candidate != oldId && used.Add(candidate))
                    return candidate;
            }
            throw new InvalidOperationException("Không tạo được ID mới (không trùng) cho: " + oldId);
        }

        private string MutateId(string id)
        {
            var chars = id.ToCharArray();
            bool changed = false;
            int i = 0;

            while (i < chars.Length)
            {
                if (!IsHex(chars[i])) { i++; continue; }

                int start = i;
                while (i < chars.Length && IsHex(chars[i])) i++;
                int len = i - start;

                bool allDigits = true, hasUpper = false;
                for (int k = start; k < i; k++)
                {
                    if (!char.IsDigit(chars[k])) allDigits = false;
                    if (char.IsUpper(chars[k])) hasUpper = true;
                }

                // Đổi: đoạn toàn chữ số, hoặc đoạn hex dài (≥ 4, kiểu GUID).
                // Không đụng đoạn ngắn có chữ cái (có thể là một phần của chữ, vd "acsm").
                if (allDigits || len >= 4)
                {
                    const string hexLower = "0123456789abcdef";
                    const string hexUpper = "0123456789ABCDEF";
                    string pool = allDigits ? "0123456789" : (hasUpper ? hexUpper : hexLower);

                    for (int k = start; k < i; k++)
                        chars[k] = pool[random.Next(pool.Length)];
                    changed = true;
                }
            }

            if (!changed)
                throw new InvalidOperationException(
                    "Không nhận ra định dạng ID để tạo ID mới: \"" + id + "\". " +
                    "Hãy gửi một ví dụ ID trong file XML để bổ sung.");

            return new string(chars);
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static void RemoveWithIndent(XmlNode node)
        {
            var parent = node.ParentNode;
            if (parent == null) return;
            if (IsWhitespace(node.PreviousSibling)) parent.RemoveChild(node.PreviousSibling);
            parent.RemoveChild(node);
        }

        private static string IndentBefore(XmlNode node)
        {
            var prev = node.PreviousSibling;
            return IsWhitespace(prev) ? prev.Value : null;
        }

        private static bool IsWhitespace(XmlNode node)
        {
            return node != null &&
                   (node.NodeType == XmlNodeType.Whitespace || node.NodeType == XmlNodeType.SignificantWhitespace);
        }

        #endregion

        #region Delete

        /// <summary>
        /// Xoá các subset / sheet (subset bị xoá kèm toàn bộ nội dung bên trong).
        /// Mục nằm bên trong một mục khác cũng bị xoá thì tự bỏ qua. Không xoá được sheet set gốc.
        /// Gọi xong thì Root đã được dựng lại.
        /// </summary>
        public void DeleteComponents(IEnumerable<SsmComponent> components)
        {
            var nodes = TopLevelNodes(components);
            foreach (var node in nodes)
                RemoveWithIndent(node);
            Reload();
        }

        /// <summary>Xoá mọi sheet bên trong (kể cả trong subset con), giữ nguyên các subset.</summary>
        public int DeleteSheetsInside(SsmComponent container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (container.Kind == SsmNodeKind.Sheet || container.Node == null)
                throw new InvalidOperationException("Chỉ áp dụng cho sheet set hoặc subset.");

            var sheets = container.Node.SelectNodes(".//AcSmSheet").Cast<XmlNode>().ToList();
            foreach (var sheet in sheets)
                RemoveWithIndent(sheet);

            Reload();
            return sheets.Count;
        }

        /// <summary>Các phần tử XML sẽ bị xoá (đã bỏ mục con của mục khác cũng bị xoá).</summary>
        public static List<XmlNode> TopLevelNodes(IEnumerable<SsmComponent> components)
        {
            if (components == null) throw new ArgumentNullException(nameof(components));

            var all = components
                .Where(c => c != null && c.Node != null)
                .Select(c =>
                {
                    if (c.Kind == SsmNodeKind.SheetSet)
                        throw new InvalidOperationException("Không xoá được sheet set gốc.");
                    return c.Node;
                })
                .Distinct()
                .ToList();

            var set = new HashSet<XmlNode>(all);
            return all.Where(n => !HasAncestorIn(n, set)).ToList();
        }

        /// <summary>
        /// Đếm số chỗ NGOÀI vùng bị xoá vẫn tham chiếu tới ID bên trong vùng bị xoá
        /// (vd: sheet selection, bảng danh mục sheet). Dùng để cảnh báo trước khi xoá.
        /// </summary>
        public int CountExternalReferences(IEnumerable<XmlNode> nodesToRemove)
        {
            var removed = new HashSet<XmlNode>(nodesToRemove);
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in removed)
            {
                foreach (XmlNode a in node.SelectNodes("descendant-or-self::*/@ID"))
                {
                    string id = a.Value.Trim();
                    if (id.Length > 0) ids.Add(id);
                }
            }
            if (ids.Count == 0) return 0;

            int count = 0;
            foreach (XmlNode n in Xml.SelectNodes("//@* | //text()"))
            {
                if (n.NodeType == XmlNodeType.Attribute && n.Name == "ID") continue;

                string value = n.Value;
                if (string.IsNullOrEmpty(value) || !ids.Contains(value.Trim())) continue;

                XmlNode owner = n.NodeType == XmlNodeType.Attribute ? ((XmlAttribute)n).OwnerElement : n.ParentNode;
                if (owner != null && !removed.Contains(owner) && !HasAncestorIn(owner, removed))
                    count++;
            }
            return count;
        }

        private static bool HasAncestorIn(XmlNode node, HashSet<XmlNode> set)
        {
            for (var p = node.ParentNode; p != null; p = p.ParentNode)
                if (set.Contains(p)) return true;
            return false;
        }

        #endregion

        #region Undo snapshot

        /// <summary>Chụp lại toàn bộ XML hiện tại (dùng cho Hoàn tác).</summary>
        public byte[] CreateSnapshot()
        {
            return Serialize();
        }

        /// <summary>Khôi phục XML từ snapshot. Gọi xong thì Root đã được dựng lại.</summary>
        public void RestoreSnapshot(byte[] snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using (var ms = new MemoryStream(snapshot))
                Xml.Load(ms); // PreserveWhitespace vẫn giữ nguyên
            Reload();
        }

        #endregion

        /// <summary>Sửa giá trị trong XML và cập nhật model.</summary>
        public void ApplyEdit(SsmComponent comp, SsmProperty prop, string value)
        {
            if (comp == null) throw new ArgumentNullException(nameof(comp));
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            if (!prop.Editable) throw new InvalidOperationException("Thuộc tính '" + prop.Name + "' không cho phép sửa.");

            value = value ?? "";
            prop.ValueNode.InnerText = value; // tự escape & < >
            prop.Value = value;

            if (prop.IsCustom) return;

            switch (prop.PropName)
            {
                case "Number": comp.Number = value; break;
                case "Title": comp.Title = value; break;
                case "Name": comp.Name = value; break;
                case "Desc": comp.Description = value; break;
            }
        }
    }
}