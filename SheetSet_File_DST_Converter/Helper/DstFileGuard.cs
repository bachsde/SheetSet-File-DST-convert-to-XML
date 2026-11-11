using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace SheetSet_File_DST_Converter.Helper
{
    /// <summary>
    /// Kiểm tra an toàn trước khi ghi file .dst khi AutoCAD / Sheet Set Manager có thể đang mở sheet set:
    /// - File bị khoá (AutoCAD đang giữ file) → không ghi, cho Retry.
    /// - File bị thay đổi từ bên ngoài kể từ lúc đọc → cảnh báo ghi đè.
    /// - AutoCAD đang chạy → nhắc đóng sheet set trong SSM (nếu không SSM có thể ghi đè file vừa build).
    /// </summary>
    public static class DstFileGuard
    {
        private static readonly string[] AutoCadProcessNames = { "acad", "acadlt" };

        /// <summary>Mã băm nội dung file, dùng để phát hiện file bị sửa từ bên ngoài.</summary>
        public static string ComputeHash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        public static string ComputeFileHash(string path)
        {
            return ComputeHash(File.ReadAllBytes(path));
        }

        /// <summary>Lý do không ghi được file (null = ghi được hoặc file chưa tồn tại).</summary>
        public static string GetWriteBlocker(string path)
        {
            if (!File.Exists(path)) return null;

            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                return "File đang ở chế độ chỉ đọc (Read-only):\n" + path;

            try
            {
                // Mở độc quyền trong chốc lát: thất bại nghĩa là chương trình khác đang giữ file
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return null;
            }
            catch (IOException)
            {
                return "File đang được chương trình khác mở (thường là AutoCAD – Sheet Set Manager):\n" + path;
            }
            catch (UnauthorizedAccessException)
            {
                return "Không có quyền ghi file:\n" + path;
            }
        }

        public static bool IsAutoCadRunning()
        {
            foreach (var name in AutoCadProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                bool running = processes.Length > 0;
                foreach (var p in processes) p.Dispose();
                if (running) return true;
            }
            return false;
        }

        /// <summary>Lặp lại cho tới khi file hết bị khoá (Retry) hoặc người dùng Cancel. Trả true nếu ghi được.</summary>
        public static bool ConfirmNotLocked(IWin32Window owner, string path)
        {
            while (true)
            {
                string blocker = GetWriteBlocker(path);
                if (blocker == null) return true;

                var answer = MessageBox.Show(owner,
                    blocker + "\n\n" +
                    "Hãy đóng sheet set trong Sheet Set Manager\n" +
                    "(chuột phải tên sheet set → Close Sheet Set), rồi bấm Retry.",
                    "File đang bị khoá", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);

                if (answer != DialogResult.Retry) return false;
            }
        }

        /// <summary>Nếu AutoCAD đang chạy: nhắc đóng sheet set trong SSM. Trả true nếu tiếp tục.</summary>
        public static bool ConfirmAutoCadClosed(IWin32Window owner)
        {
            if (!IsAutoCadRunning()) return true;

            var answer = MessageBox.Show(owner,
                "AutoCAD đang chạy.\n\n" +
                "Nếu sheet set này đang mở trong Sheet Set Manager, hãy đóng nó trước khi ghi file " +
                "(chuột phải tên sheet set → Close Sheet Set).\n" +
                "Nếu không, SSM có thể ghi đè lên file vừa build và làm mất thay đổi.\n\n" +
                "Đã đóng sheet set trong SSM (hoặc không mở) — tiếp tục ghi file?",
                "AutoCAD đang chạy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            return answer == DialogResult.Yes;
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
