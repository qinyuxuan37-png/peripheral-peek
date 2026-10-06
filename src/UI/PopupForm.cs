using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeripheralPeek.UI
{
    internal sealed class PopupForm : Form
    {
        private const int HeaderHeight = 50;
        private const int RowHeight = 58;
        private const int OpenDurationMs = 210;
        private const int OpenTravelPx = 12;
        private const int CloseDurationMs = 165;
        private const int CloseTravelPx = 9;
        private readonly List<PeripheralStatus> _devices = new List<PeripheralStatus>();
        private readonly Timer _openTimer;
        private readonly Stopwatch _openClock = new Stopwatch();
        private readonly Timer _closeTimer;
        private readonly Stopwatch _closeClock = new Stopwatch();
        private Point _openTarget;
        private Point _closeStart;
        private double _closeStartOpacity;
        private bool _isClosing;
        private Bitmap _backdrop;
        private string _selectedId;
        private Rectangle _refreshBounds;
        private int _hoverRow = -1;
        private bool _refreshHover;
        private bool _lightTheme;
        private Color _surface;
        private Color _surfaceTop;
        private Color _surfaceBottom;
        private Color _primary;
        private Color _secondary;
        private Color _divider;
        private Color _hover;
        private Color _selected;
        private Color _accent;
        private Color _iconSurface;
        private Color _border;

        public event EventHandler<DeviceSelectedEventArgs> DeviceSelected;
        public event EventHandler RefreshRequested;

        public PopupForm()
        {
            Width = 378;
            Height = 136;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "PeripheralPeek.DevicePopup";
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Padding = new Padding(0);
            _openTimer = new Timer { Interval = 15 };
            _openTimer.Tick += AdvanceOpeningAnimation;
            _closeTimer = new Timer { Interval = 15 };
            _closeTimer.Tick += AdvanceClosingAnimation;
            ApplyTheme(Color.Empty);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDwmTheme();
        }

        public void UpdateDevices(IList<PeripheralStatus> devices, string selectedId)
        {
            _devices.Clear();
            _devices.AddRange(devices);
            _selectedId = selectedId;
            int rows = Math.Max(1, Math.Min(7, _devices.Count));
            int newHeight = HeaderHeight + rows * RowHeight + 12;
            if (!_isClosing && Visible && Height != newHeight)
            {
                StopOpeningAnimation();
                int bottom = Bottom;
                Height = newHeight;
                Location = new Point(Left, bottom - Height);
                _openTarget = Location;
            }
            else if (!_isClosing) Height = newHeight;
            UpdateRegion();
            Invalidate();
        }

        public void ShowNearTray()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle area = screen.WorkingArea;
            ApplyTheme(Color.Empty);
            ShowAnimated(new Point(area.Right - Width - 10, area.Bottom - Height - 16));
        }

        public void ShowNear(Rectangle anchor)
        {
            Screen screen = Screen.FromRectangle(anchor);
            Rectangle area = screen.WorkingArea;
            ApplyTheme(TaskbarStatusForm.GetTaskbarColorNear(anchor));
            int x = anchor.Left + (anchor.Width - Width) / 2;
            x = Math.Max(area.Left + 8, Math.Min(x, area.Right - Width - 8));
            int y;
            if (anchor.Top >= area.Bottom - 2)
                y = area.Bottom - Height - 16;
            else if (anchor.Bottom <= area.Top + 2)
                y = area.Top + 16;
            else
                y = anchor.Top - Height - 16;
            y = Math.Max(area.Top + 8, Math.Min(y, area.Bottom - Height - 8));
            ShowAnimated(new Point(x, y));
        }

        private void ShowAnimated(Point target)
        {
            StopOpeningAnimation();
            _openTarget = target;
            CaptureBackdrop(target);
            Location = new Point(target.X, target.Y + OpenTravelPx);
            Opacity = 0.02;
            Show();
            Activate();
            _openClock.Restart();
            _openTimer.Start();
        }

        public void CloseAnimated()
        {
            if (!Visible || _isClosing) return;
            _openTimer.Stop();
            _openClock.Reset();
            _closeStart = Location;
            _closeStartOpacity = Opacity;
            _isClosing = true;
            _closeClock.Restart();
            _closeTimer.Start();
        }

        private void AdvanceClosingAnimation(object sender, EventArgs e)
        {
            if (!Visible)
            {
                StopClosingAnimation();
                return;
            }

            double progress = Math.Min(1.0, _closeClock.Elapsed.TotalMilliseconds / CloseDurationMs);
            double ease = progress * progress * (3.0 - 2.0 * progress);
            Location = new Point(_closeStart.X, _closeStart.Y + (int)Math.Round(CloseTravelPx * ease));
            Opacity = Math.Max(0.02, _closeStartOpacity * (1.0 - ease));
            if (progress >= 1.0) Hide();
        }

        private void StopClosingAnimation()
        {
            _closeTimer.Stop();
            _closeClock.Reset();
            _isClosing = false;
        }

        private void AdvanceOpeningAnimation(object sender, EventArgs e)
        {
            if (!Visible)
            {
                StopOpeningAnimation();
                return;
            }

            double progress = Math.Min(1.0, _openClock.Elapsed.TotalMilliseconds / OpenDurationMs);
            double remaining = 1.0 - progress;
            double movement = 1.0 - remaining * remaining * remaining;
            double fade = 1.0 - remaining * remaining;
            Location = new Point(_openTarget.X, _openTarget.Y + (int)Math.Round(OpenTravelPx * (1.0 - movement)));
            Opacity = 0.02 + 0.98 * fade;
            if (progress >= 1.0) StopOpeningAnimation();
        }

        private void StopOpeningAnimation()
        {
            _openTimer.Stop();
            _openClock.Reset();
            Opacity = 1.0;
            if (Visible) Location = _openTarget;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                StopClosingAnimation();
                StopOpeningAnimation();
                DisposeBackdrop();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _openTimer.Dispose();
                _closeTimer.Dispose();
                DisposeBackdrop();
            }
            base.Dispose(disposing);
        }

        private void CaptureBackdrop(Point target)
        {
            DisposeBackdrop();
            try
            {
                using (Bitmap screen = new Bitmap(Width, Height))
                using (Graphics source = Graphics.FromImage(screen))
                {
                    source.CopyFromScreen(target, Point.Empty, screen.Size);
                    _backdrop = new Bitmap(Math.Max(1, Width / 14), Math.Max(1, Height / 14));
                    using (Graphics blurred = Graphics.FromImage(_backdrop))
                    {
                        blurred.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        blurred.DrawImage(screen, new Rectangle(0, 0, _backdrop.Width, _backdrop.Height));
                    }
                }
            }
            catch
            {
                DisposeBackdrop();
            }
        }

        private void DisposeBackdrop()
        {
            if (_backdrop == null) return;
            _backdrop.Dispose();
            _backdrop = null;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            CloseAnimated();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_backdrop != null)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(_backdrop, ClientRectangle);
            }
            Color top = _backdrop == null ? _surfaceTop : Color.FromArgb(_lightTheme ? 244 : 237, _surfaceTop);
            Color bottom = _backdrop == null ? _surfaceBottom : Color.FromArgb(_lightTheme ? 244 : 237, _surfaceBottom);
            using (LinearGradientBrush surfaceBrush = new LinearGradientBrush(ClientRectangle, top, bottom, LinearGradientMode.Vertical))
                graphics.FillRectangle(surfaceBrush, ClientRectangle);
            using (Font titleFont = new Font(Font.FontFamily, 11f, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(_primary))
                graphics.DrawString("外设", titleFont, titleBrush, 18, 14);

            _refreshBounds = new Rectangle(Width - 43, 9, 30, 30);
            if (_refreshHover) DrawRoundRect(graphics, _refreshBounds, 8, _hover);
            using (Font refreshFont = new Font("Segoe UI Symbol", 11.5f))
            using (SolidBrush secondary = new SolidBrush(_secondary))
            using (StringFormat center = CenterFormat())
                graphics.DrawString("↻", refreshFont, secondary, _refreshBounds, center);

            using (Pen divider = new Pen(_divider)) graphics.DrawLine(divider, 16, HeaderHeight - 1, Width - 16, HeaderHeight - 1);

            if (_devices.Count == 0)
            {
                Rectangle empty = new Rectangle(18, HeaderHeight, Width - 36, RowHeight);
                using (SolidBrush secondary = new SolidBrush(_secondary))
                using (StringFormat center = CenterFormat())
                    graphics.DrawString("暂无已连接外设", Font, secondary, empty, center);
            }
            else
            {
                int count = Math.Min(7, _devices.Count);
                for (int i = 0; i < count; i++) DrawDevice(graphics, _devices[i], i);
            }

            using (GraphicsPath borderPath = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (Pen border = new Pen(_border)) graphics.DrawPath(border, borderPath);
        }

        private void DrawDevice(Graphics graphics, PeripheralStatus device, int index)
        {
            int top = HeaderHeight + index * RowHeight;
            Rectangle row = new Rectangle(7, top + 3, Width - 14, RowHeight - 6);
            bool selected = String.Equals(device.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
            if (selected) DrawRoundRect(graphics, row, 9, _selected);
            else if (_hoverRow == index) DrawRoundRect(graphics, row, 9, _hover);

            if (selected)
            {
                Rectangle indicator = new Rectangle(row.Left + 2, row.Top + 13, 3, row.Height - 26);
                DrawRoundRect(graphics, indicator, 2, _accent);
            }

            Rectangle icon = new Rectangle(18, top + 14, 30, 30);
            DrawRoundRect(graphics, icon, 8, _iconSurface);
            using (Font iconFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            using (SolidBrush iconText = new SolidBrush(_accent))
            using (StringFormat center = CenterFormat())
                graphics.DrawString(device.KindGlyph, iconFont, iconText, icon, center);

            using (Font nameFont = new Font(Font.FontFamily, 9.25f, FontStyle.Regular))
            using (SolidBrush primary = new SolidBrush(_primary))
                graphics.DrawString(TrimTo(device.Name, 28), nameFont, primary, 60, top + 10);

            string subtitle = device.ConnectionText;
            if (device.IsSleeping) subtitle += " · 休眠";
            if (device.IsCharging == true) subtitle += " · 充电中";
            using (Font subFont = new Font(Font.FontFamily, 8f))
            using (SolidBrush secondary = new SolidBrush(_secondary))
                graphics.DrawString(subtitle, subFont, secondary, 60, top + 33);

            Color valueColor = BatteryColor(device);
            using (Font valueFont = new Font("Microsoft YaHei UI", 9.25f, FontStyle.Regular))
            using (SolidBrush valueBrush = new SolidBrush(valueColor))
            using (StringFormat right = new StringFormat())
            {
                right.Alignment = StringAlignment.Far;
                right.LineAlignment = StringAlignment.Center;
                graphics.DrawString(device.ValueText, valueFont, valueBrush, new Rectangle(247, top + 10, 112, 34), right);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool refresh = _refreshBounds.Contains(e.Location);
            int row = -1;
            if (e.Y >= HeaderHeight)
            {
                int candidate = (e.Y - HeaderHeight) / RowHeight;
                if (candidate >= 0 && candidate < _devices.Count && candidate < 7) row = candidate;
            }
            if (refresh != _refreshHover || row != _hoverRow)
            {
                _refreshHover = refresh;
                _hoverRow = row;
                Cursor = refresh || row >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _refreshHover = false;
            _hoverRow = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isClosing) return;
            if (e.Button != MouseButtons.Left) return;
            if (_refreshBounds.Contains(e.Location))
            {
                EventHandler refresh = RefreshRequested;
                if (refresh != null) refresh(this, EventArgs.Empty);
                return;
            }
            int index = (e.Y - HeaderHeight) / RowHeight;
            if (index >= 0 && index < _devices.Count && index < 7)
            {
                EventHandler<DeviceSelectedEventArgs> selected = DeviceSelected;
                if (selected != null) selected(this, new DeviceSelectedEventArgs(_devices[index]));
            }
        }

        private void ApplyTheme(Color taskbarColor)
        {
            _lightTheme = IsLightTaskbarTheme();
            Color fallback = _lightTheme ? Color.FromArgb(243, 247, 251) : Color.FromArgb(32, 32, 32);
            Color baseColor = taskbarColor.IsEmpty ? fallback : taskbarColor;
            int luminance = (baseColor.R * 299 + baseColor.G * 587 + baseColor.B * 114) / 1000;
            _lightTheme = luminance >= 128;
            _surface = Blend(baseColor, _lightTheme ? Color.White : Color.Black, _lightTheme ? 0.10f : 0.04f);
            _surfaceTop = Blend(_surface, _lightTheme ? Color.White : Color.FromArgb(53, 53, 56), _lightTheme ? 0.18f : 0.22f);
            _surfaceBottom = Blend(_surface, _lightTheme ? Color.Black : Color.Black, _lightTheme ? 0.025f : 0.08f);
            _primary = _lightTheme ? Color.FromArgb(28, 28, 30) : Color.FromArgb(245, 245, 247);
            _secondary = _lightTheme ? Color.FromArgb(96, 99, 104) : Color.FromArgb(171, 174, 180);
            _divider = _lightTheme ? Color.FromArgb(210, 216, 224) : Color.FromArgb(61, 63, 68);
            _border = _lightTheme ? Color.FromArgb(204, 210, 218) : Color.FromArgb(79, 81, 86);
            _accent = GetWindowsAccentColor();
            if (!_lightTheme && GetLuminance(_accent) < 105) _accent = Color.FromArgb(96, 165, 250);
            _hover = Blend(_surface, _primary, _lightTheme ? 0.055f : 0.08f);
            _selected = Blend(_surface, _accent, _lightTheme ? 0.115f : 0.18f);
            _iconSurface = Blend(_surface, _accent, _lightTheme ? 0.13f : 0.22f);
            BackColor = _surface;
            ApplyDwmTheme();
            Invalidate();
        }

        private void ApplyDwmTheme()
        {
            if (!IsHandleCreated) return;
            int dark = _lightTheme ? 0 : 1;
            int rounded = 2;
            try
            {
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
            }
            catch { }
        }

        private void UpdateRegion()
        {
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private static void DrawRoundRect(Graphics graphics, Rectangle rectangle, int radius, Color color)
        {
            using (GraphicsPath path = RoundedPath(rectangle, radius))
            using (SolidBrush brush = new SolidBrush(color)) graphics.FillPath(brush, path);
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

        private static StringFormat CenterFormat()
        {
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            return format;
        }

        private Color BatteryColor(PeripheralStatus device)
        {
            if (!device.BatteryPercent.HasValue) return _secondary;
            if (device.BatteryPercent.Value <= 15) return Color.FromArgb(196, 43, 28);
            if (device.BatteryPercent.Value <= 30) return Color.FromArgb(154, 103, 0);
            return _accent;
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        private static int GetLuminance(Color color)
        {
            return (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        }

        private static Color GetWindowsAccentColor()
        {
            try
            {
                uint value;
                bool opaque;
                if (DwmGetColorizationColor(out value, out opaque) == 0)
                    return Color.FromArgb((int)((value >> 16) & 0xFF), (int)((value >> 8) & 0xFF), (int)(value & 0xFF));
            }
            catch { }
            return Color.FromArgb(0, 95, 184);
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

        private static string TrimTo(string value, int max)
        {
            if (String.IsNullOrEmpty(value)) return "未命名外设";
            return value.Length <= max ? value : value.Substring(0, max - 1) + "…";
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetColorizationColor(out uint colorization, out bool opaqueBlend);
    }

    internal sealed class DeviceSelectedEventArgs : EventArgs
    {
        public PeripheralStatus Device { get; private set; }
        public DeviceSelectedEventArgs(PeripheralStatus device) { Device = device; }
    }
}
