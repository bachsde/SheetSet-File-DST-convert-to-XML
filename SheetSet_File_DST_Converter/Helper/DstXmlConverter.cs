using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace SheetSet_File_DST_Converter.Helper
{
    /// <summary>
    /// Mã hoá / giải mã file Sheet Set (.dst) của AutoCAD.
    /// File .dst là XML (UTF-8) trong đó MỖI BYTE được thay bằng một byte khác theo một bảng cố định 1-1.
    /// Vì vậy chỉ cần bảng 256 byte, không cần map theo từng ký tự UTF-8,
    /// và độ dài file không đổi sau khi chuyển đổi.
    /// </summary>
    public sealed class DstXmlConverter
    {
        private const int MaxReportedItems = 20;

        private readonly byte[] xmlToDst = new byte[256];
        private readonly byte[] dstToXml = new byte[256];
        private readonly bool[] knownXml = new bool[256];
        private readonly bool[] knownDst = new bool[256];

        /// <summary>Số byte (trên 256) đã biết cách chuyển đổi.</summary>
        public int KnownByteCount
        {
            get { return knownXml.Count(k => k); }
        }

        /// <summary>
        /// Tạo converter gồm bảng built-in, gộp thêm file dictionary nếu có.
        /// </summary>
        public static DstXmlConverter Create(string dictionaryPath)
        {
            var converter = new DstXmlConverter();
            converter.LoadBuiltIn();

            if (!string.IsNullOrWhiteSpace(dictionaryPath))
            {
                if (!File.Exists(dictionaryPath))
                    throw new FileNotFoundException("Không tìm thấy file dictionary:\n" + dictionaryPath);
                converter.LoadDictionary(dictionaryPath);
            }
            return converter;
        }

        #region Load / Save / Learn

        public void LoadBuiltIn()
        {
            var pairs = BuiltInDictionary.XmlToDstPairs;
            for (int i = 0; i < pairs.GetLength(0); i++)
                AddPair(pairs[i, 0], pairs[i, 1], "built-in");
        }

        /// <summary>
        /// Gộp một file dictionary JSON vào bảng hiện tại.
        /// Đọc được cả định dạng mới (theo byte) lẫn định dạng cũ (theo ký tự UTF-8 ghép thành số nguyên).
        /// </summary>
        public void LoadDictionary(string path)
        {
            string source = Path.GetFileName(path);

            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var root = doc.RootElement;
                bool any = false;
                JsonElement section;

                if (root.TryGetProperty("xml_to_dst", out section))
                {
                    LoadSection(section, true, source);
                    any = true;
                }
                if (root.TryGetProperty("dst_to_xml", out section))
                {
                    LoadSection(section, false, source);
                    any = true;
                }
                if (!any)
                    throw new InvalidDataException(
                        "File dictionary '" + source + "' không có mục \"xml_to_dst\" hoặc \"dst_to_xml\".");
            }
        }

        /// <summary>
        /// Lưu bảng hiện tại ra JSON (theo byte). File này vẫn đọc được bằng phiên bản tool cũ.
        /// </summary>
        public void SaveDictionary(string path)
        {
            var options = new JsonWriterOptions { Indented = true };

            using (var fs = File.Create(path))
            using (var w = new Utf8JsonWriter(fs, options))
            {
                w.WriteStartObject();
                w.WriteString("format", "dst-byte-map");
                w.WriteNumber("version", 2);
                w.WriteNumber("known_bytes", KnownByteCount);

                w.WriteStartObject("xml_to_dst");
                for (int b = 0; b < 256; b++)
                    if (knownXml[b]) w.WriteNumber(b.ToString(), xmlToDst[b]);
                w.WriteEndObject();

                w.WriteStartObject("dst_to_xml");
                for (int b = 0; b < 256; b++)
                    if (knownDst[b]) w.WriteNumber(b.ToString(), dstToXml[b]);
                w.WriteEndObject();

                w.WriteEndObject();
            }
        }

        /// <summary>
        /// Học bảng mã từ một cặp file: XML phải là bản giải mã NGUYÊN GỐC của file DST (chưa chỉnh sửa).
        /// Trả về số byte mới học được.
        /// </summary>
        public int Learn(byte[] xml, byte[] dst)
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));
            if (dst == null) throw new ArgumentNullException(nameof(dst));

            if (xml.Length != dst.Length)
                throw new InvalidDataException(
                    $"Hai file không cùng độ dài (XML: {xml.Length:N0} byte, DST: {dst.Length:N0} byte).\n" +
                    "File XML phải là bản giải mã nguyên gốc của chính file DST này, chưa chỉnh sửa và cùng kiểu BOM.");

            int added = 0;
            for (int i = 0; i < xml.Length; i++)
            {
                if (AddPair(xml[i], dst[i], "vị trí " + i))
                    added++;
            }
            return added;
        }

        #endregion

        #region Convert

        public byte[] XmlToDst(byte[] xml)
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));

            var output = new byte[xml.Length];
            var unknown = new List<string>();
            var seen = new HashSet<string>();
            int line = 1;

            for (int i = 0; i < xml.Length; i++)
            {
                byte b = xml[i];
                if (knownXml[b])
                {
                    output[i] = xmlToDst[b];
                }
                else
                {
                    string ch = DescribeXmlChar(xml, i);
                    if (seen.Add(ch))
                        unknown.Add(ch + " (dòng " + line + ")");
                }
                if (b == 0x0A) line++;
            }

            if (unknown.Count > 0)
                throw new UnmappedBytesException(BuildUnknownMessage(
                    "File XML có ký tự chưa có trong dictionary:", unknown));

            return output;
        }

        public byte[] DstToXml(byte[] dst)
        {
            if (dst == null) throw new ArgumentNullException(nameof(dst));

            var output = new byte[dst.Length];
            var unknown = new List<string>();
            var seen = new HashSet<byte>();

            for (int i = 0; i < dst.Length; i++)
            {
                byte b = dst[i];
                if (knownDst[b])
                    output[i] = dstToXml[b];
                else if (seen.Add(b))
                    unknown.Add($"byte 0x{b:X2} (vị trí {i})");
            }

            if (unknown.Count > 0)
                throw new UnmappedBytesException(BuildUnknownMessage(
                    "File DST có byte chưa có trong dictionary:", unknown));

            return output;
        }

        #endregion

        /// <summary>
        /// Kiểm tra một chuỗi (sẽ ghi vào XML) có mã hoá sang DST được không.
        /// Dùng để chặn ngay lúc người dùng nhập ký tự chưa có trong dictionary.
        /// </summary>
        public bool CanEncodeText(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text)) return true;

            // Dạng sẽ được ghi trong XML (XmlWriter escape & < > trong nội dung text)
            string escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            try
            {
                XmlToDst(Encoding.UTF8.GetBytes(escaped));
                return true;
            }
            catch (UnmappedBytesException ex)
            {
                error = ex.Message.Replace(" (dòng 1)", "");
                return false;
            }
        }

        #region XML validation

        /// <summary>
        /// Kiểm tra và chuẩn hoá XML trước khi mã hoá sang DST:
        /// - Từ chối UTF-16.
        /// - Bỏ BOM UTF-8 nếu bảng mã chưa biết các byte BOM (editor hay tự thêm BOM).
        /// - Kiểm tra XML hợp lệ (well-formed).
        /// </summary>
        public byte[] PrepareXmlForEncoding(byte[] xml, out bool bomRemoved)
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));
            bomRemoved = false;

            if (xml.Length >= 2 &&
                ((xml[0] == 0xFF && xml[1] == 0xFE) || (xml[0] == 0xFE && xml[1] == 0xFF)))
                throw new InvalidDataException(
                    "File XML đang được lưu dạng UTF-16. Hãy lưu lại dạng UTF-8 rồi thử lại.");

            if (xml.Length >= 3 && xml[0] == 0xEF && xml[1] == 0xBB && xml[2] == 0xBF &&
                !(knownXml[0xEF] && knownXml[0xBB] && knownXml[0xBF]))
            {
                var trimmed = new byte[xml.Length - 3];
                Buffer.BlockCopy(xml, 3, trimmed, 0, trimmed.Length);
                xml = trimmed;
                bomRemoved = true;
            }

            string error;
            if (!IsWellFormedXml(xml, out error))
                throw new InvalidDataException("XML không hợp lệ (" + error + ").");

            return xml;
        }

        public static bool IsWellFormedXml(byte[] xml, out string error)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null
                };
                using (var ms = new MemoryStream(xml))
                using (var reader = XmlReader.Create(ms, settings))
                {
                    while (reader.Read()) { }
                }
                error = null;
                return true;
            }
            catch (XmlException ex)
            {
                error = $"dòng {ex.LineNumber}, cột {ex.LinePosition}: {ex.Message}";
                return false;
            }
        }

        #endregion

        #region Helpers

        /// <summary>Thêm một cặp byte, báo lỗi nếu xung đột với bảng hiện có. Trả về true nếu là byte mới.</summary>
        private bool AddPair(byte x, byte d, string source)
        {
            if (knownXml[x])
            {
                if (xmlToDst[x] != d)
                    throw new InvalidDataException(
                        $"Xung đột dictionary ({source}): byte XML 0x{x:X2} đã map sang 0x{xmlToDst[x]:X2}, nay lại là 0x{d:X2}.");
                return false;
            }
            if (knownDst[d])
                throw new InvalidDataException(
                    $"Xung đột dictionary ({source}): byte DST 0x{d:X2} đã là mã của byte XML 0x{dstToXml[d]:X2}, không thể là mã của 0x{x:X2}.");

            xmlToDst[x] = d;
            dstToXml[d] = x;
            knownXml[x] = true;
            knownDst[d] = true;
            return true;
        }

        private void LoadSection(JsonElement section, bool keyIsXml, string source)
        {
            foreach (var prop in section.EnumerateObject())
            {
                long key;
                if (!long.TryParse(prop.Name, out key))
                    throw new InvalidDataException($"Key không hợp lệ trong {source}: \"{prop.Name}\"");

                long value = prop.Value.GetInt64();
                long xmlCode = keyIsXml ? key : value;
                long dstCode = keyIsXml ? value : key;
                AddCodePair(xmlCode, dstCode, source);
            }
        }

        /// <summary>
        /// Định dạng cũ ghép các byte của 1 ký tự UTF-8 thành số nguyên (big-endian),
        /// ví dụ 'Ú' = C3 9A = 50074 → 49 36 = 18742. Tách lại thành từng cặp byte.
        /// </summary>
        private void AddCodePair(long xmlCode, long dstCode, string source)
        {
            int n = ByteLength(xmlCode);
            if (xmlCode < 0 || dstCode < 0 || ByteLength(dstCode) > n)
                throw new InvalidDataException($"Entry không hợp lệ trong {source}: {xmlCode} → {dstCode}");

            for (int j = 0; j < n; j++)
            {
                int shift = 8 * (n - 1 - j);
                AddPair((byte)(xmlCode >> shift), (byte)(dstCode >> shift), source);
            }
        }

        private static int ByteLength(long v)
        {
            if (v <= 0xFF) return 1;
            if (v <= 0xFFFF) return 2;
            if (v <= 0xFFFFFF) return 3;
            return 4;
        }

        private static int Utf8Length(byte lead)
        {
            if (lead < 0x80) return 1;
            if ((lead & 0xE0) == 0xC0) return 2;
            if ((lead & 0xF0) == 0xE0) return 3;
            if ((lead & 0xF8) == 0xF0) return 4;
            return 0; // byte nối tiếp hoặc không hợp lệ
        }

        /// <summary>Mô tả ký tự UTF-8 chứa byte tại vị trí i, để báo lỗi dễ hiểu.</summary>
        private static string DescribeXmlChar(byte[] s, int i)
        {
            int start = i;
            while (start > 0 && i - start < 3 && (s[start] & 0xC0) == 0x80)
                start--;

            int len = Utf8Length(s[start]);
            if (len == 0 || start + len > s.Length)
                return $"byte 0x{s[i]:X2}";

            if (len == 1 && s[start] < 0x20)
                return s[start] == 0x09 ? "Tab (0x09)" : $"ký tự điều khiển 0x{s[start]:X2}";

            string text = Encoding.UTF8.GetString(s, start, len);
            return $"'{text}' (U+{char.ConvertToUtf32(text, 0):X4})";
        }

        private static string BuildUnknownMessage(string header, List<string> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine(header);
            foreach (var item in items.Take(MaxReportedItems))
                sb.AppendLine("  • " + item);
            if (items.Count > MaxReportedItems)
                sb.AppendLine($"  … và {items.Count - MaxReportedItems} mục khác.");
            sb.AppendLine();
            sb.Append("Cách khắc phục: vào menu Công cụ → \"Tạo dictionary...\" với một cặp file DST + XML mẫu " +
                      "có chứa các ký tự này (chương trình sẽ dùng luôn dictionary vừa tạo).");
            return sb.ToString();
        }

        #endregion
    }

    /// <summary>Gặp byte/ký tự chưa có trong bảng mã.</summary>
    public sealed class UnmappedBytesException : Exception
    {
        public UnmappedBytesException(string message) : base(message) { }
    }
}
