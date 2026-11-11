using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SheetSet_File_DST_Converter.Helper
{
    /// <summary>Một mục trong danh sách đã chọn (sheet, subset hoặc sheet set).</summary>
    public sealed class SelectionEntry
    {
        public SsmNodeKind Kind { get; set; }
        public string Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Name { get; set; }

        /// <summary>Chữ hiển thị khi báo lỗi: sheet = "Số - Tên", còn lại = tên.</summary>
        public string Display
        {
            get
            {
                if (Kind != SsmNodeKind.Sheet) return string.IsNullOrEmpty(Name) ? "(không tên)" : Name;
                if (string.IsNullOrEmpty(Number)) return Title ?? "";
                if (string.IsNullOrEmpty(Title)) return Number;
                return Number + " - " + Title;
            }
        }
    }

    /// <summary>
    /// Lưu / nạp danh sách các mục đã chọn trong sheet set ra file JSON.
    /// Khi nạp: ghép theo ID trước (chính xác nhất); không có thì ghép theo số hiệu sheet / tên subset.
    /// </summary>
    public static class SelectionList
    {
        private const string FormatName = "sheetset-selection";

        public static void Save(string path, string sheetSetName, string sourceFile, IEnumerable<SsmComponent> selected)
        {
            if (selected == null) throw new ArgumentNullException(nameof(selected));

            var options = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // giữ nguyên chữ tiếng Việt, dễ đọc
            };

            using (var fs = File.Create(path))
            using (var w = new Utf8JsonWriter(fs, options))
            {
                w.WriteStartObject();
                w.WriteString("format", FormatName);
                w.WriteNumber("version", 1);
                w.WriteString("sheetSet", sheetSetName ?? "");
                w.WriteString("sourceFile", sourceFile ?? "");
                w.WriteString("savedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                w.WriteStartArray("items");
                foreach (var c in selected)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", c.Kind.ToString());
                    w.WriteString("id", c.Id ?? "");
                    if (c.Kind == SsmNodeKind.Sheet)
                    {
                        w.WriteString("number", c.Number ?? "");
                        w.WriteString("title", c.Title ?? "");
                    }
                    else
                    {
                        w.WriteString("name", c.Name ?? "");
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteEndObject();
            }
        }

        public static List<SelectionEntry> Load(string path)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var root = doc.RootElement;

                JsonElement format;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("format", out format) ||
                    format.ValueKind != JsonValueKind.String ||
                    format.GetString() != FormatName)
                    throw new InvalidDataException("Đây không phải file danh sách chọn do chương trình này tạo ra.");

                JsonElement items;
                if (!root.TryGetProperty("items", out items) || items.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("File danh sách chọn không có mục \"items\".");

                var list = new List<SelectionEntry>();
                foreach (var el in items.EnumerateArray())
                {
                    SsmNodeKind kind;
                    if (!Enum.TryParse(Str(el, "kind"), out kind)) continue;

                    list.Add(new SelectionEntry
                    {
                        Kind = kind,
                        Id = Str(el, "id"),
                        Number = Str(el, "number"),
                        Title = Str(el, "title"),
                        Name = Str(el, "name")
                    });
                }
                return list;
            }
        }

        /// <summary>
        /// Ghép các mục đã lưu với sheet set đang mở. Trả về các mục tìm được (theo thứ tự trong file);
        /// notFound là các mục không ghép được (hoặc ghép mơ hồ vì trùng số hiệu / tên).
        /// </summary>
        public static List<SsmComponent> Resolve(SsmComponent root, IEnumerable<SelectionEntry> entries,
                                                 out List<SelectionEntry> notFound)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var all = new List<SsmComponent>();
            Flatten(root, all);

            var byId = new Dictionary<string, SsmComponent>(StringComparer.Ordinal);
            foreach (var c in all)
            {
                if (!string.IsNullOrEmpty(c.Id) && !byId.ContainsKey(c.Id))
                    byId[c.Id] = c;
            }

            var result = new List<SsmComponent>();
            var seen = new HashSet<SsmComponent>();
            notFound = new List<SelectionEntry>();

            foreach (var entry in entries)
            {
                SsmComponent match = null;
                SsmComponent byIdMatch;

                if (!string.IsNullOrEmpty(entry.Id) && byId.TryGetValue(entry.Id, out byIdMatch) &&
                    byIdMatch.Kind == entry.Kind)
                    match = byIdMatch;
                else if (entry.Kind == SsmNodeKind.Sheet)
                    match = FindSheet(all, entry);
                else if (entry.Kind == SsmNodeKind.SheetSet)
                    match = root;
                else
                    match = FindSubset(all, entry);

                if (match == null) notFound.Add(entry);
                else if (seen.Add(match)) result.Add(match);
            }
            return result;
        }

        private static SsmComponent FindSheet(List<SsmComponent> all, SelectionEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Number)) return null;

            var candidates = all
                .Where(c => c.Kind == SsmNodeKind.Sheet && Same(c.Number, entry.Number))
                .ToList();

            // Trùng số hiệu → thu hẹp bằng tên sheet
            if (candidates.Count > 1)
                candidates = candidates.Where(c => Same(c.Title, entry.Title)).ToList();

            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static SsmComponent FindSubset(List<SsmComponent> all, SelectionEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Name)) return null;

            var candidates = all
                .Where(c => c.Kind == SsmNodeKind.Subset && Same(c.Name, entry.Name))
                .ToList();

            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.CurrentCultureIgnoreCase);
        }

        private static void Flatten(SsmComponent node, List<SsmComponent> result)
        {
            result.Add(node);
            foreach (var child in node.Children) Flatten(child, result);
        }

        private static string Str(JsonElement el, string name)
        {
            JsonElement v;
            return el.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
        }
    }
}
