using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PeripheralPeek.UI
{
    internal static class IconRenderer
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Render(PeripheralStatus status)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                graphics.Clear(Color.Transparent);

                Color textColor = GetTaskbarTextColor();
                string text = "—";
                if (status != null)
                {
                    text = status.TrayText;
                }

                float fontSize = text.Length >= 3 ? 16f : (text.Length == 2 ? 21f : 22f);
                string fontName = ContainsCjk(text) ? "Microsoft YaHei UI" : "Segoe UI";
                using (Font font = new Font(fontName, fontSize, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush brush = new SolidBrush(textColor))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    graphics.DrawString(text, font, brush, new RectangleF(0, -1, 32, 33), format);
                }

                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private static Color GetTaskbarTextColor()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    if (value is int && (int)value == 0) return Color.White;
                }
            }
            catch { }
            return Color.FromArgb(24, 24, 24);
        }

        private static bool ContainsCjk(string text)
        {
            foreach (char value in text)
            {
                if (value >= 0x2E80) return true;
            }
            return false;
        }
    }
}
