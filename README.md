# SheetSet File DST convert to XML (.dst ↔ .xml)

Ứng dụng WinForms (C# .NET 8) giúp **convert lại các file .DST của AutoCAD SheetSet** thành định dạng XML dễ đọc và chỉnh sửa.

### 🛠️ Được phát triển dựa trên tài liệu:
https://gist.github.com/mxwell/e253548692820cdce778631165090080#file-dst_format_ctl-py

---

## 🚀 Tính năng chính

| Nút | Chức năng |
|-----|-----------|
| **Browse DST** | Chọn file `.dst` cần convert |
| **Browse XML** | Chọn file `.xml` nguồn vào hoặc xuất |
| **Browse Dictionary** | Chọn `dictionary.json` nếu bạn có mã code dịch (mặc định mã code theo Tiếng Việt)|
| **Generate Dictionary** | Dùng `.dst` và `.xml` gốc để tạo file `dictionary.json` mã code dịch |
| **DST → XML** | Convert `.dst` sang `.xml`  |
| **XML → DST** | Convert từ `.xml` sang `.dst` |
| **Export Excel** | Trích xuất thông tin trong `.xml` ra file Excel (.xlsx) đầy đủ |
| **Load XML → TreeView** | Hiển thị toàn bộ cấu trúc XML lên `TreeView` |

---

## 🔍 Yêu cầu

- .NET 8.0 (đã upgrade từ .NET Framework)
- ClosedXML để xuất Excel (`ClosedXML`, `DocumentFormat.OpenXml`)
- Newtonsoft.Json nếu bạn dùng cả deserialize linh hoạt (tùy chọn)

---

## 📂 Cấu trúc Dictionary

File `dictionary.json` chứa :
- `"dst_to_xml"`: dùng khi convert `.dst` sang `.xml`
- `"xml_to_dst"`: dùng khi convert `.xml` thành `.dst`

Bạn có thể tạo file này từ một cặp `.dst` & `.xml` đã biết bằng nút **Generate Dictionary**.

---

## 📝 Excel Output Format

- Sheet ID
- Subset Name / Description
- Sheet Number, Title, Revision Info, Category, etc.
- Layout Name, Path (Full/Relative/UNC/Env)
- File Name
- Các **custom properties** trong SheetSet sẽ tự động xuất ra ở những cột cuối cùng trong file excel

---

## 📌 Ghi chú kỹ thuật

- Ký tự được giải mã/mã hóa theo **đơn vị codepoint UTF-8**, không phải đơn byte
- Dictionary được ánh xạ từng mã ký tự → mã thay thế
- Nếu không chọn file dictionary, ứng dụng sẽ dùng bản `dictionary.json` có sẵn

---

## 🤝 Cảm ơn

Phân tích ban đầu từ: [@mxwell's gist](https://gist.github.com/mxwell/e253548692820cdce778631165090080)

---

## 📷 Screenshot (tuỳ chọn)

![screenshot](https://github.com/user-attachments/assets/1eb44604-892a-4112-9d91-4fcfbaa3eaaa)
![image](https://github.com/user-attachments/assets/c2acf27e-79f9-48dc-a258-826afcb4dd72)

---

## 📜 License

MIT License.
