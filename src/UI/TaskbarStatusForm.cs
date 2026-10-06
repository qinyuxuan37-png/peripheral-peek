using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeripheralPeek.UI
{
    internal sealed class TaskbarStatusForm : Form
    {
        private const int GwlStyle = -16;
        private const int GwlHwndParent = -8;
        private const int WsChild = 0x40000000;
        private const int WsPopup = unchecked((int)0x80000000);
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private const uint WmDisplayChange = 0x007E;
        private const uint WmSettingChange = 0x001A;
        private const uint WmDpiChanged = 0x02E0;
        private const uint WmThemeChanged = 0x031A;
        private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        private readonly Timer _positionTimer;
        private readonly ToolTip _toolTip;
        private readonly bool _embedInTaskbar;
        private IntPtr _taskbarHandle;
        private string _displayText = "外设 —";
        private string _description = "暂无已连接外设";
        private bool _lightTheme;
        private bool _hovered;
        private bool _popupOpen;
        private Color _taskbarBackColor;

        public event EventHandler PrimaryClick;
        public event EventHandler DeviceTopologyChanged;

        public TaskbarStatusForm() : this(true) { }

        internal TaskbarStatusForm(bool embedInTaskbar)
        {
            _embedInTaskbar = embedInTaskbar;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Width = 78;
            Height = 36;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Cursor = Cursors.Hand;
            Text = "PeripheralPeek.TaskbarStatus";

            _positionTimer = new Timer();
            _positionTimer.Interval = 1200;
            _positionTimer.Tick += delegate { AttachAndPosition(); };
            _toolTip = new ToolTip();
            _toolTip.SetToolTip(this, _description);
            ApplyTheme();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return parameters;
            }
        }

        public Rectangle ScreenBounds
        {
            get
            {
                NativeRect rectangle;
                if (IsHandleCreated && GetWindowRect(Handle, out rectangle))
                    return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
                return Bounds;
            }
        }

        public void UpdateStatus(PeripheralStatus status)
        {
            _displayText = status == null ? "外设 —" : status.TaskbarDisplayText;
            _description = status == null
                ? "暂无已连接外设"
                : status.Name + " · " + status.ValueText + " · " + status.ConnectionText;
            _toolTip.SetToolTip(this, _description);

            RecalculateWidth();
            ApplyTheme();
            Invalidate();
            if (_embedInTaskbar && IsHandleCreated) AttachAndPosition();
        }

        public void SetPopupOpen(bool open)
        {
            if (_popupOpen == open) return;
            _popupOpen = open;
            Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!_embedInTaskbar) return;
            RecalculateWidth();
            AttachAndPosition();
            _positionTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _positionTimer.Stop();
            _toolTip.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            EventHandler handler = PrimaryClick;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 16 || Height < 16) return;
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 7))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0219) // WM_DEVICECHANGE
            {
                long reason = message.WParam.ToInt64();
                if (reason != 0x0007 && reason != 0x8000 && reason != 0x8004) return;
                EventHandler handler = DeviceTopologyChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            else if (message.Msg == TaskbarCreatedMessage || message.Msg == WmDisplayChange ||
                     message.Msg == WmSettingChange || message.Msg == WmDpiChanged || message.Msg == WmThemeChanged)
            {
                if (message.Msg == WmDpiChanged || message.Msg == WmDisplayChange) RecalculateWidth();
                if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) AttachAndPosition(); });
            }
        }

        private void RecalculateWidth()
        {
            double scale = GetScale();
            Size measured = TextRenderer.MeasureText(_displayText, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Width = Math.Max((int)Math.Round(68 * scale), Math.Min((int)Math.Round(176 * scale), measured.Width + (int)Math.Round(22 * scale)));
        }

        private double GetScale()
        {
            uint dpi = IsHandleCreated ? GetDpiForWindow(Handle) : 96;
            return Math.Max(1.0, Math.Min(3.0, dpi / 96.0));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color background = _taskbarBackColor;
            if (_popupOpen || _hovered)
                background = Blend(background, _lightTheme ? Color.White : Color.White,
                    _lightTheme ? (_popupOpen ? 0.48f : 0.30f) : (_popupOpen ? 0.16f : 0.10f));
            Color foreground = _lightTheme ? Color.FromArgb(25, 25, 25) : Color.FromArgb(246, 246, 246);
            using (SolidBrush backgroundBrush = new SolidBrush(background)) graphics.FillRectangle(backgroundBrush, ClientRectangle);
            using (SolidBrush textBrush = new SolidBrush(foreground))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                graphics.DrawString(_displayText, Font, textBrush, new RectangleF(7, 0, Width - 14, Height), format);
            }
        }

        private void ApplyTheme()
        {
            _lightTheme = IsLightTaskbarTheme();
            _taskbarBackColor = _lightTheme ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
            BackColor = _taskbarBackColor;
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void AttachAndPosition()
        {
            if (!_embedInTaskbar || IsDisposed) return;
            IntPtr taskbar = FindTaskbarWithTray();
            if (taskbar == IntPtr.Zero)
            {
                if (Visible) Hide();
                _taskbarHandle = IntPtr.Zero;
                return;
            }

            if (_taskbarHandle != taskbar)
            {
                int style = GetWindowLongPtr(Handle, GwlStyle).ToInt32();
                style = (style & ~WsChild) | WsPopup;
                SetWindowLongPtr(Handle, GwlStyle, new IntPtr(style));
                SetWindowLongPtr(Handle, GwlHwndParent, taskbar);
                _taskbarHandle = taskbar;
            }

            NativeRect taskbarRect;
            if (!GetWindowRect(taskbar, out taskbarRect)) return;
            IntPtr tray = FindDescendantByClass(taskbar, "TrayNotifyWnd");
            NativeRect trayRect = new NativeRect();
            bool hasTray = tray != IntPtr.Zero && GetWindowRect(tray, out trayRect);
            if (!IsTaskbarSurfaceVisible(taskbar))
            {
                Hide();
                return;
            }

            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            bool horizontal = taskbarWidth >= taskbarHeight;
            int x;
            int y;
            double scale = GetScale();

            if (horizontal)
            {
                int targetHeight = Math.Max(20, Math.Min((int)Math.Round(38 * scale), taskbarHeight - 4));
                if (Height != targetHeight) Height = targetHeight;
                int trayLeft = hasTray ? trayRect.Left : taskbarRect.Right;
                x = Math.Max(taskbarRect.Left + 2, trayLeft - Width - (int)Math.Round(6 * scale));
                y = taskbarRect.Top + Math.Max(0, (taskbarHeight - Height) / 2);
            }
            else
            {
                int targetWidth = Math.Max(24, Math.Min((int)Math.Round(42 * scale), taskbarWidth - 4));
                if (Width > targetWidth) Width = targetWidth;
                x = taskbarRect.Left + Math.Max(0, (taskbarWidth - Width) / 2);
                int trayTop = hasTray ? trayRect.Top : taskbarRect.Bottom;
                y = Math.Max(taskbarRect.Top + 2, trayTop - Height - 6);
            }

            Color sampled = SampleScreenColor(horizontal ? x - 3 : x + Width / 2, horizontal ? y + Height / 2 : y - 3);
            bool themeChanged = false;
            if (!sampled.IsEmpty && sampled != _taskbarBackColor)
            {
                _taskbarBackColor = sampled;
                BackColor = sampled;
                _lightTheme = (sampled.R * 299 + sampled.G * 587 + sampled.B * 114) / 1000 >= 128;
                themeChanged = true;
            }
            NativeRect currentRect;
            bool moved = !GetWindowRect(Handle, out currentRect) || currentRect.Left != x || currentRect.Top != y ||
                currentRect.Right - currentRect.Left != Width || currentRect.Bottom - currentRect.Top != Height;
            SetWindowPos(Handle, new IntPtr(-1), x, y, Width, Height, SwpNoActivate | SwpShowWindow);
            if (themeChanged || moved) Invalidate();
        }

        private static IntPtr FindTaskbarWithTray()
        {
            IntPtr primary = FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero && FindDescendantByClass(primary, "TrayNotifyWnd") != IntPtr.Zero) return primary;
            IntPtr secondary = IntPtr.Zero;
            while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                if (FindDescendantByClass(secondary, "TrayNotifyWnd") != IntPtr.Zero) return secondary;
            return primary;
        }

        private static bool IsTaskbarSurfaceVisible(IntPtr taskbar)
        {
            if (!IsWindowVisible(taskbar)) return false;
            // An unrelated popup can cover a point in the tray without hiding
            // the taskbar. Query Windows' fullscreen/presentation state instead.
            int state;
            if (SHQueryUserNotificationState(out state) != 0) return true;
            return state != 1 && state != 2 && state != 3 && state != 4;
        }

        private static Color SampleScreenColor(int x, int y)
        {
            IntPtr device = GetDC(IntPtr.Zero);
            if (device == IntPtr.Zero) return Color.Empty;
            uint color = GetPixel(device, x, y);
            ReleaseDC(IntPtr.Zero, device);
            if (color == 0xFFFFFFFF) return Color.Empty;
            return Color.FromArgb((int)(color & 0xFF), (int)((color >> 8) & 0xFF), (int)((color >> 16) & 0xFF));
        }

        private static IntPtr FindDescendantByClass(IntPtr parent, string className)
        {
            IntPtr result = IntPtr.Zero;
            EnumChildWindows(parent, delegate(IntPtr window, IntPtr parameter)
            {
                char[] buffer = new char[128];
                int length = GetClassName(window, buffer, buffer.Length);
                if (length > 0 && String.Equals(new string(buffer, 0, length), className, StringComparison.Ordinal))
                {
                    result = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static bool IsLightTaskbarTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    return value == null || Convert.ToInt32(value) != 0;
                }
            }
            catch { return true; }
        }

        internal static Rectangle GetTaskbarScreenBounds()
        {
            IntPtr taskbar = FindTaskbarWithTray();
            NativeRect rectangle;
            if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out rectangle))
                return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
            return Rectangle.Empty;
        }

        internal static Color GetTaskbarColorNear(Rectangle anchor)
        {
            int x = anchor.Left - 4;
            int y = anchor.Top + Math.Max(1, anchor.Height / 2);
            Color sampled = SampleScreenColor(x, y);
            if (!sampled.IsEmpty) return sampled;
            return IsLightTaskbarTheme() ? Color.FromArgb(243, 247, 251) : Color.FromArgb(32, 32, 32);
        }

        internal static Rectangle GetLiveWidgetScreenBounds()
        {
            return GetTopLevelWindowBounds("PeripheralPeek.TaskbarStatus");
        }

        internal static Rectangle GetTopLevelWindowBounds(string title)
        {
            IntPtr widget = FindWindow(null, title);
            NativeRect rectangle;
            if (widget != IntPtr.Zero && GetWindowRect(widget, out rectangle))
                return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
            return Rectangle.Empty;
        }

        internal static bool SendTestPrimaryClick()
        {
            IntPtr widget = FindWindow(null, "PeripheralPeek.TaskbarStatus");
            if (widget == IntPtr.Zero) return false;
            IntPtr point = new IntPtr((18 << 16) | 24);
            return PostMessage(widget, 0x0201, new IntPtr(1), point) &&
                   PostMessage(widget, 0x0202, IntPtr.Zero, point);
        }

        internal static bool IsTopLevelWindowVisible(string title)
        {
            IntPtr window = FindWindow(null, title);
            return window != IntPtr.Zero && IsWindowVisible(window);
        }

        internal static bool SendTestDeviceChange()
        {
            IntPtr widget = FindWindow(null, "PeripheralPeek.TaskbarStatus");
            return widget != IntPtr.Zero && PostMessage(widget, 0x0219, new IntPtr(0x0007), IntPtr.Zero);
        }

        internal static bool SendTestDisplayChange()
        {
            IntPtr widget = FindWindow(null, "PeripheralPeek.TaskbarStatus");
            return widget != IntPtr.Zero && PostMessage(widget, WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
        }

        internal static bool SendTestTaskbarCreated()
        {
            IntPtr widget = FindWindow(null, "PeripheralPeek.TaskbarStatus");
            return widget != IntPtr.Zero && PostMessage(widget, TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero);
        }

        private static IntPtr FindDescendantByTitle(IntPtr parent, string title)
        {
            IntPtr result = IntPtr.Zero;
            EnumChildWindows(parent, delegate(IntPtr window, IntPtr parameter)
            {
                char[] buffer = new char[256];
                int length = GetWindowText(window, buffer, buffer.Length);
                if (length > 0 && String.Equals(new string(buffer, 0, length), title, StringComparison.Ordinal))
                {
                    result = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, char[] className, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, char[] text, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        private static IntPtr GetWindowLongPtr(IntPtr window, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr window, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

        private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(window, index, value) : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr device);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr device, int x, int y);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);
    }
}
