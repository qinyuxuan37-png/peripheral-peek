using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeripheralPeek.UI
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly TaskbarStatusForm _taskbarStatus;
        private readonly PopupForm _popup;
        private readonly DeviceMonitor _monitor;
        private readonly SettingsStore _store;
        private readonly AppSettings _settings;
        private readonly Random _random = new Random();
        private string _randomDeviceId;
        private string _displayedDeviceId;

        public TrayApplicationContext()
        {
            _store = new SettingsStore();
            _settings = _store.Load();
            _popup = new PopupForm();
            _popup.DeviceSelected += OnDeviceSelected;
            _popup.RefreshRequested += delegate { _monitor.RefreshNow(true); };

            _taskbarStatus = new TaskbarStatusForm();
            _popup.VisibleChanged += delegate { _taskbarStatus.SetPopupOpen(_popup.Visible); };
            _taskbarStatus.PrimaryClick += delegate { ShowPopup(); };
            _taskbarStatus.ContextMenuStrip = BuildMenu();
            _taskbarStatus.UpdateStatus(null);
            _taskbarStatus.Show();

            _monitor = new DeviceMonitor();
            _monitor.DevicesChanged += OnDevicesChanged;
            _taskbarStatus.DeviceTopologyChanged += delegate { _monitor.NotifyDeviceTopologyChanged(); };
            _monitor.Start(_settings.RefreshSeconds);
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("打开外设状态");
            open.Click += delegate { ShowPopup(); };
            menu.Items.Add(open);

            ToolStripMenuItem refresh = new ToolStripMenuItem("立即刷新");
            refresh.Click += delegate { _monitor.RefreshNow(true); };
            menu.Items.Add(refresh);

            ToolStripMenuItem random = new ToolStripMenuItem("随机选择任务栏状态");
            random.Click += delegate
            {
                _settings.HasManualSelection = false;
                _settings.SelectedDeviceId = null;
                _settings.SelectedPhysicalId = null;
                _settings.SelectedVendorId = 0;
                _settings.SelectedProductId = 0;
                _settings.SelectedKind = null;
                _randomDeviceId = null;
                _store.Save(_settings);
                UpdateUi();
            };
            menu.Items.Add(random);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem startup = new ToolStripMenuItem("开机启动");
            startup.Checked = StartupManager.IsEnabled();
            startup.Click += delegate
            {
                bool enabled = !StartupManager.IsEnabled();
                StartupManager.SetEnabled(enabled);
                startup.Checked = enabled;
                _settings.StartWithWindows = enabled;
                _store.Save(_settings);
            };
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApplication(); };
            menu.Items.Add(exit);
            return menu;
        }

        private void ShowPopup()
        {
            UpdateUi();
            if (_popup.Visible) _popup.CloseAnimated();
            else _popup.ShowNear(_taskbarStatus.ScreenBounds);
        }

        private void OnDevicesChanged(object sender, EventArgs e)
        {
            UpdateUi();
        }

        private void OnDeviceSelected(object sender, DeviceSelectedEventArgs e)
        {
            _settings.HasManualSelection = true;
            _settings.SelectedDeviceId = e.Device.Id;
            _settings.SelectedPhysicalId = e.Device.PhysicalId;
            _settings.SelectedVendorId = e.Device.VendorId;
            _settings.SelectedProductId = e.Device.ProductId;
            _settings.SelectedKind = e.Device.Kind.ToString();
            _store.Save(_settings);
            UpdateUi();
        }

        private void UpdateUi()
        {
            IList<PeripheralStatus> devices = _monitor.Current;
            PeripheralStatus selected = SelectDisplayDevice(devices);
            _displayedDeviceId = selected == null ? null : selected.Id;
            _popup.UpdateDevices(devices, _displayedDeviceId);

            _taskbarStatus.UpdateStatus(selected);
        }

        private PeripheralStatus SelectDisplayDevice(IList<PeripheralStatus> devices)
        {
            if (_settings.HasManualSelection && !String.IsNullOrEmpty(_settings.SelectedDeviceId))
            {
                PeripheralStatus manual = devices.FirstOrDefault(d => String.Equals(d.Id, _settings.SelectedDeviceId, StringComparison.OrdinalIgnoreCase));
                if (manual != null) return manual;
                if (!String.IsNullOrEmpty(_settings.SelectedPhysicalId))
                {
                    manual = devices.FirstOrDefault(d => String.Equals(d.PhysicalId, _settings.SelectedPhysicalId, StringComparison.OrdinalIgnoreCase));
                    if (manual != null) return manual;
                }
                if (_settings.SelectedVendorId != 0)
                {
                    manual = devices.FirstOrDefault(d => d.VendorId == _settings.SelectedVendorId && d.ProductId == _settings.SelectedProductId && d.Kind.ToString() == _settings.SelectedKind);
                    if (manual != null) return manual;
                }
            }

            if (!String.IsNullOrEmpty(_randomDeviceId))
            {
                PeripheralStatus existing = devices.FirstOrDefault(d => String.Equals(d.Id, _randomDeviceId, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing;
            }

            List<PeripheralStatus> candidates = devices.Where(d => d.HasBattery).ToList();
            if (candidates.Count == 0) candidates = devices.ToList();
            if (candidates.Count == 0)
            {
                _randomDeviceId = null;
                return null;
            }
            PeripheralStatus selected = candidates[_random.Next(candidates.Count)];
            _randomDeviceId = selected.Id;
            return selected;
        }

        private void ExitApplication()
        {
            _monitor.Dispose();
            _popup.Dispose();
            _taskbarStatus.Close();
            _taskbarStatus.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _monitor.Dispose(); } catch { }
                try { _taskbarStatus.Dispose(); } catch { }
                try { _popup.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PeripheralPeek";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return key != null && key.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }
}
