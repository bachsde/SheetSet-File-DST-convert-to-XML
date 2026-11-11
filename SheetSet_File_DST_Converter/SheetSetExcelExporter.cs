using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace SheetSet_File_DST_Converter.Helper
{
    /// <summary>
    /// Xuất danh sách sheet trong XML Sheet Set ra Excel.
    /// Worksheet "Sheets": mỗi sheet một dòng. Worksheet "Sheet Set": thuộc tính cấp sheet set.
    /// </summary>
    public static class SheetSetExcelExporter
    {
        // Thuộc tính chuẩn của AcSmSheet → tên cột
        private static readonly string[] SheetPropNames =
        {
            "Number", "Title", "Desc", "IncludeForPublish", "RevisionNumber",
            "RevisionDate", "Purpose", "Category", "Folder"
        };
        private static readonly string[] SheetPropColumns =
        {
            "Sheet number", "Sheet title", "Description", "Include for publish", "Revision number",
            "Revision date", "Purpose", "Category", "Folder"
        };

        private static readonly string[] FixedHeaders =
        {
            "Sheet ID", "Subset Path", "Subset Name", "Subset Description",
            "Sheet number", "Sheet title", "Description", "Include for publish",
            "Revision number", "Revision date", "Purpose", "Category", "Folder",
            "Layout", "File Name", "Relative Path", "Environment Path", "UNC Path", "Full Path"
        };

        private static readonly HashSet<string> FixedHeaderSet =
            new HashSet<string>(FixedHeaders, StringComparer.Ordinal);

        /// <summary>Xuất ra file Excel, trả về số sheet đã xuất.</summary>
        public static int Export(XmlDocument doc, string excelPath)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var rows = new List<Dictionary<string, string>>();
            foreach (XmlNode sheet in doc.SelectNodes("//AcSmSheet"))
                rows.Add(ReadSheet(sheet));

            var dynamicHeaders = rows
                .SelectMany(r => r.Keys)
                .Where(k => !FixedHeaderSet.Contains(k))
                .Distinct()
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var headers = FixedHeaders.Concat(dynamicHeaders).ToList();

            using (var wb = new XLWorkbook())
            {
                WriteTable(wb.Worksheets.Add("Sheets"), headers, rows);

                var setRows = ReadSheetSetProps(doc);
                if (setRows.Count > 0)
                    WriteTable(wb.Worksheets.Add("Sheet Set"),
                        new List<string> { "Property", "Value", "Scope" }, setRows);

                wb.SaveAs(excelPath);
            }
            return rows.Count;
        }

        private static Dictionary<string, string> ReadSheet(XmlNode sheet)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            row["Sheet ID"] = Attr(sheet, "ID");

            // Subset: lấy subset cha trực tiếp + đường dẫn đầy đủ nếu lồng nhau
            var subsetNames = new List<string>();
            XmlNode immediateSubset = null;
            for (XmlNode p = sheet.ParentNode; p != null; p = p.ParentNode)
            {
                if (p.Name != "AcSmSubset") continue;
                if (immediateSubset == null) immediateSubset = p;
                subsetNames.Insert(0, Prop(p, "Name"));
            }
            if (immediateSubset != null)
            {
                row["Subset Path"] = string.Join(" / ", subsetNames);
                row["Subset Name"] = Prop(immediateSubset, "Name");
                row["Subset Description"] = Prop(immediateSubset, "Desc");
            }

            // Thuộc tính chuẩn
            for (int i = 0; i < SheetPropNames.Length; i++)
                row[SheetPropColumns[i]] = Prop(sheet, SheetPropNames[i]);

            // Layout
            var layout = sheet.SelectSingleNode("AcSmAcDbLayoutReference");
            if (layout != null)
            {
                string fullPath = Prop(layout, "FileName");
                row["Layout"] = Prop(layout, "Name");
                row["File Name"] = SafeGetFileName(fullPath);
                row["Relative Path"] = Prop(layout, "Relative_FileName");
                row["Environment Path"] = Prop(layout, "Environ_FileName");
                row["UNC Path"] = Prop(layout, "Unc_FileName");
                row["Full Path"] = fullPath;
            }

            // Custom property (trong Bag hoặc trực tiếp).
            // Nếu trùng tên cột chuẩn thì thêm hậu tố để không ghi đè giá trị chuẩn.
            foreach (XmlNode custom in sheet.SelectNodes(
                         "AcSmCustomPropertyBag/AcSmCustomPropertyValue | AcSmCustomPropertyValue"))
            {
                string name = Attr(custom, "propname");
                if (string.IsNullOrEmpty(name)) continue;

                string key = FixedHeaderSet.Contains(name) ? name + " (custom)" : name;
                row[key] = Prop(custom, "Value");
            }

            return row;
        }

        /// <summary>
        /// Thuộc tính cấp Sheet Set. Flags của custom property:
        /// bit 1 = thuộc tính Sheet Set, bit 2 = thuộc tính Sheet (giá trị mặc định cho sheet mới).
        /// </summary>
        private static List<Dictionary<string, string>> ReadSheetSetProps(XmlDocument doc)
        {
            var list = new List<Dictionary<string, string>>();
            var set = doc.SelectSingleNode("//AcSmSheetSet");
            if (set == null) return list;

            list.Add(MakeRow("(Name)", Prop(set, "Name"), "Sheet Set"));
            list.Add(MakeRow("(Description)", Prop(set, "Desc"), "Sheet Set"));

            foreach (XmlNode custom in set.SelectNodes("AcSmCustomPropertyBag/AcSmCustomPropertyValue"))
            {
                string name = Attr(custom, "propname");
                if (string.IsNullOrEmpty(name)) continue;

                string flagsText = Prop(custom, "Flags");
                int flags;
                string scope;
                if (int.TryParse(flagsText, out flags))
                    scope = (flags & 1) != 0 ? "Sheet Set"
                          : (flags & 2) != 0 ? "Sheet (mặc định)"
                          : "Flags = " + flags;
                else
                    scope = flagsText;

                list.Add(MakeRow(name, Prop(custom, "Value"), scope));
            }
            return list;
        }

        private static void WriteTable(IXLWorksheet ws, IList<string> headers, List<Dictionary<string, string>> rows)
        {
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.SetValue(headers[c]);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
            }

            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < headers.Count; c++)
                {
                    string val;
                    rows[r].TryGetValue(headers[c], out val);
                    // SetValue(string) giữ nguyên dạng text (ví dụ số hiệu sheet "01" không bị đổi thành 1)
                    ws.Cell(r + 2, c + 1).SetValue(val ?? "");
                }
            }

            ws.SheetView.FreezeRows(1);
            ws.Range(1, 1, Math.Max(rows.Count, 1) + 1, headers.Count).SetAutoFilter();

            foreach (var col in ws.ColumnsUsed())
            {
                col.AdjustToContents();
                if (col.Width > 100) col.Width = 100;
            }
        }

        private static Dictionary<string, string> MakeRow(string property, string value, string scope)
        {
            return new Dictionary<string, string>
            {
                ["Property"] = property,
                ["Value"] = value,
                ["Scope"] = scope
            };
        }

        private static string Prop(XmlNode node, string propname)
        {
            return node.SelectSingleNode($"AcSmProp[@propname='{propname}']")?.InnerText?.Trim() ?? "";
        }

        private static string Attr(XmlNode node, string name)
        {
            return node.Attributes?[name]?.Value?.Trim() ?? "";
        }

        private static string SafeGetFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return Path.GetFileName(path); }
            catch (ArgumentException) { return path; }
        }
    }
}
