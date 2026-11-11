using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SheetSet_File_DST_Converter.Helper
{
    /// <summary>Một thay đổi sẽ được áp dụng khi import từ Excel.</summary>
    public sealed class ImportChange
    {
        public SsmComponent Component { get; set; }
        public SsmProperty Property { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public sealed class ImportResult
    {
        public List<ImportChange> Changes { get; } = new List<ImportChange>();
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Số dòng dữ liệu có nội dung trong worksheet.</summary>
        public int RowsRead { get; set; }

        /// <summary>Số dòng khớp được với một sheet trong sheet set (theo Sheet ID).</summary>
        public int RowsMatched { get; set; }

        /// <summary>Số dòng không khớp được sheet nào (thiếu ID, hoặc sheet đã bị xoá).</summary>
        public int RowsNotFound { get; set; }
    }

    /// <summary>
    /// Đọc file Excel do chương trình xuất ra (worksheet "Sheets"), so với sheet set đang mở
    /// và trả về danh sách thay đổi. Chỉ cập nhật sheet có sẵn (ghép theo cột "Sheet ID"),
    /// không tạo / xoá sheet.
    /// </summary>
    public static class SheetSetExcelImporter
    {
        private const string SheetsWorksheetName = "Sheets";
        private const string IdHeader = "Sheet ID";
        private const string CustomSuffix = " (custom)";

        // Cột chuẩn có thể sửa → propname trong XML
        private static readonly Dictionary<string, string> StandardColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sheet number"] = "Number",
                ["Sheet title"] = "Title",
                ["Description"] = "Desc",
                ["Revision number"] = "RevisionNumber",
                ["Revision date"] = "RevisionDate",
                ["Purpose"] = "Purpose",
                ["Category"] = "Category"
            };

        // Cột chỉ để tham khảo, không import
        private static readonly HashSet<string> ReadOnlyColumns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Subset Path", "Subset Name", "Subset Description", "Include for publish", "Folder",
                "Layout", "File Name", "Relative Path", "Environment Path", "UNC Path", "Full Path"
            };

        private sealed class Column
        {
            public int Index;
            public string Header;
            public string PropName; // null nếu là custom property
            public bool IsCustom { get { return PropName == null; } }
        }

        /// <param name="validate">Trả về thông báo lỗi nếu giá trị không dùng được (vd: ký tự chưa có trong dictionary), null nếu hợp lệ.</param>
        public static ImportResult Read(string excelPath, SsmComponent root, Func<string, string> validate)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var result = new ImportResult();

            var sheetsById = new Dictionary<string, SsmComponent>(StringComparer.Ordinal);
            CollectSheets(root, sheetsById);

            // FileShare.ReadWrite: đọc được cả khi file đang mở trong Excel (đã Ctrl+S)
            using (var fs = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var wb = new XLWorkbook(fs))
            {
                IXLWorksheet ws = null;
                foreach (IXLWorksheet w in wb.Worksheets)
                {
                    if (string.Equals(w.Name, SheetsWorksheetName, StringComparison.OrdinalIgnoreCase))
                    {
                        ws = w;
                        break;
                    }
                }
                if (ws == null) ws = wb.Worksheets.FirstOrDefault();
                if (ws == null) throw new InvalidDataException("File Excel không có worksheet nào.");

                var lastCol = ws.LastColumnUsed();
                var lastRow = ws.LastRowUsed();
                if (lastCol == null || lastRow == null)
                    throw new InvalidDataException("Worksheet \"" + ws.Name + "\" đang trống.");

                int colCount = lastCol.ColumnNumber();
                int rowCount = lastRow.RowNumber();

                // ---- Dòng tiêu đề
                int idCol = 0;
                var columns = new List<Column>();
                for (int c = 1; c <= colCount; c++)
                {
                    string header = CellText(ws.Cell(1, c));
                    if (header.Length == 0) continue;

                    if (string.Equals(header, IdHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        idCol = c;
                        continue;
                    }
                    if (ReadOnlyColumns.Contains(header)) continue;

                    string propName;
                    columns.Add(new Column
                    {
                        Index = c,
                        Header = header,
                        PropName = StandardColumns.TryGetValue(header, out propName) ? propName : null
                    });
                }

                if (idCol == 0)
                    throw new InvalidDataException(
                        "Không tìm thấy cột \"" + IdHeader + "\" ở dòng đầu của worksheet \"" + ws.Name + "\".\n" +
                        "Hãy import file Excel do chương trình này xuất ra, và đừng xoá cột \"" + IdHeader + "\".");

                // ---- Các dòng dữ liệu
                for (int r = 2; r <= rowCount; r++)
                {
                    string id = CellText(ws.Cell(r, idCol));

                    if (id.Length == 0)
                    {
                        if (RowHasData(ws, r, columns))
                        {
                            result.RowsRead++;
                            result.RowsNotFound++;
                            result.Warnings.Add($"Dòng {r}: không có Sheet ID → bỏ qua " +
                                                "(chương trình chỉ cập nhật sheet có sẵn, không tạo sheet mới).");
                        }
                        continue;
                    }

                    result.RowsRead++;

                    SsmComponent comp;
                    if (!sheetsById.TryGetValue(id, out comp))
                    {
                        result.RowsNotFound++;
                        result.Warnings.Add($"Dòng {r}: không tìm thấy sheet có ID \"{id}\" (đã bị xoá khỏi sheet set?) → bỏ qua.");
                        continue;
                    }

                    result.RowsMatched++;

                    foreach (var col in columns)
                        ReadCell(ws, r, col, comp, validate, result);
                }
            }

            return result;
        }

        private static void ReadCell(IXLWorksheet ws, int row, Column col, SsmComponent comp,
                                     Func<string, string> validate, ImportResult result)
        {
            string newValue = CellText(ws.Cell(row, col.Index));

            SsmProperty prop = col.IsCustom ? FindCustom(comp, col.Header) : FindStandard(comp, col.PropName);
            if (prop == null)
            {
                if (newValue.Length > 0)
                    result.Warnings.Add($"Dòng {row} ({comp.DisplayText}): sheet này không có thuộc tính " +
                                        $"\"{col.Header}\" → bỏ qua giá trị \"{newValue}\".");
                return;
            }

            if (newValue == prop.Value) return; // không đổi

            if (newValue.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                result.Warnings.Add($"Dòng {row} ({comp.DisplayText}), cột \"{col.Header}\": giá trị có xuống dòng → bỏ qua.");
                return;
            }

            string error = validate == null ? null : validate(newValue);
            if (error != null)
            {
                result.Warnings.Add($"Dòng {row} ({comp.DisplayText}), cột \"{col.Header}\": {Shorten(error)} → bỏ qua.");
                return;
            }

            result.Changes.Add(new ImportChange
            {
                Component = comp,
                Property = prop,
                OldValue = prop.Value,
                NewValue = newValue
            });
        }

        private static SsmProperty FindStandard(SsmComponent comp, string propName)
        {
            return comp.Properties.FirstOrDefault(p => p.Editable && !p.IsCustom && p.PropName == propName);
        }

        /// <summary>
        /// Tìm custom property theo tên cột. Cột "X (custom)" là tên chương trình thêm vào khi custom property
        /// trùng tên cột chuẩn → thử cả tên "X".
        /// </summary>
        private static SsmProperty FindCustom(SsmComponent comp, string header)
        {
            var exact = comp.Properties.FirstOrDefault(p => p.IsCustom && p.Editable && p.PropName == header);
            if (exact != null) return exact;

            if (header.EndsWith(CustomSuffix, StringComparison.Ordinal))
            {
                string stripped = header.Substring(0, header.Length - CustomSuffix.Length);
                return comp.Properties.FirstOrDefault(p => p.IsCustom && p.Editable && p.PropName == stripped);
            }
            return null;
        }

        private static void CollectSheets(SsmComponent node, Dictionary<string, SsmComponent> result)
        {
            if (node.Kind == SsmNodeKind.Sheet && !string.IsNullOrEmpty(node.Id) && !result.ContainsKey(node.Id))
                result[node.Id] = node;

            foreach (var child in node.Children)
                CollectSheets(child, result);
        }

        private static bool RowHasData(IXLWorksheet ws, int row, List<Column> columns)
        {
            foreach (var col in columns)
                if (CellText(ws.Cell(row, col.Index)).Length > 0) return true;
            return false;
        }

        private static string CellText(IXLCell cell)
        {
            string text = cell.GetFormattedString();
            return text == null ? "" : text.Trim();
        }

        /// <summary>Rút gọn thông báo lỗi nhiều dòng của dictionary thành một dòng ngắn.</summary>
        private static string Shorten(string error)
        {
            var lines = error.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var bullets = lines.Where(l => l.StartsWith("•", StringComparison.Ordinal))
                               .Select(l => l.TrimStart('•', ' '))
                               .ToList();

            if (bullets.Count > 0)
                return "ký tự chưa có trong dictionary: " + string.Join(", ", bullets);
            return lines.Count > 0 ? lines[0] : error;
        }
    }
}
