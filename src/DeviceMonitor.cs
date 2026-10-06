using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PeripheralPeek.Providers;

namespace PeripheralPeek
{
    internal sealed class DeviceMonitor : IDisposable
    {
        private readonly List<IPeripheralProvider> _providers;
        private readonly object _gate = new object();
        private readonly SynchronizationContext _context;
        private Timer _timer;
        private Timer _topologyTimer;
        private int _isScanning;
        private int _pendingRescan;
        private bool _disposed;
        private List<PeripheralStatus> _current = new List<PeripheralStatus>();

        public event EventHandler DevicesChanged;

        public DeviceMonitor()
        {
            _context = SynchronizationContext.Current;
            _providers = new List<IPeripheralProvider>
            {
                new GenericHidProvider(),
                new XInputProvider(),
                new BluetoothProvider(),
                new LogitechHidppProvider()
            };
        }

        public IList<PeripheralStatus> Current
        {
            get
            {
                lock (_gate) return _current.Select(d => d.Clone()).ToList();
            }
        }

        public void Start(int refreshSeconds)
        {
            _timer = new Timer(delegate { RefreshNow(false); }, null, 0, Math.Max(3, refreshSeconds) * 1000);
            _topologyTimer = new Timer(delegate { RefreshNow(true); }, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void NotifyDeviceTopologyChanged()
        {
            if (_disposed) return;
            Timer timer = _topologyTimer;
            if (timer != null) timer.Change(500, Timeout.Infinite);
        }

        public void RefreshNow()
        {
            RefreshNow(false);
        }

        public void RefreshNow(bool invalidateCaches)
        {
            if (_disposed) return;
            if (invalidateCaches)
            {
                foreach (IPeripheralProvider provider in _providers)
                {
                    ICacheInvalidatable cache = provider as ICacheInvalidatable;
                    if (cache != null) cache.InvalidateCache();
                }
            }
            if (Interlocked.CompareExchange(ref _isScanning, 1, 0) != 0)
            {
                if (invalidateCaches) Interlocked.Exchange(ref _pendingRescan, 1);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<PeripheralStatus> found = new List<PeripheralStatus>();
                    DeviceScanSnapshot snapshot = new DeviceScanSnapshot();
                    foreach (IPeripheralProvider provider in _providers)
                    {
                        try
                        {
                            ISharedScanProvider shared = provider as ISharedScanProvider;
                            found.AddRange(shared == null ? provider.Scan() : shared.Scan(snapshot));
                        }
                        catch (Exception ex) { ProviderLog.Write(provider.Name, ex); }
                    }
                    List<PeripheralStatus> merged = Merge(found);
                    bool changed;
                    lock (_gate)
                    {
                        changed = !SameVisibleStatuses(_current, merged);
                        _current = merged;
                    }
                    if (changed) RaiseChanged();
                }
                finally
                {
                    Interlocked.Exchange(ref _isScanning, 0);
                    if (Interlocked.Exchange(ref _pendingRescan, 0) != 0) RefreshNow(true);
                }
            });
        }

        private static bool SameVisibleStatuses(IList<PeripheralStatus> left, IList<PeripheralStatus> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                PeripheralStatus a = left[i];
                PeripheralStatus b = right[i];
                if (!String.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                    a.Kind != b.Kind || a.Connection != b.Connection ||
                    a.BatteryPercent != b.BatteryPercent ||
                    !String.Equals(a.BatteryLevelText, b.BatteryLevelText, StringComparison.Ordinal) ||
                    a.IsCharging != b.IsCharging || a.IsExternallyPowered != b.IsExternallyPowered ||
                    a.IsSleeping != b.IsSleeping)
                    return false;
            }
            return true;
        }

        internal List<PeripheralStatus> Merge(List<PeripheralStatus> found)
        {
            List<PeripheralStatus> previous;
            lock (_gate) previous = _current.Select(d => d.Clone()).ToList();

            List<PeripheralStatus> output = new List<PeripheralStatus>();
            foreach (PeripheralStatus item in found.OrderBy(d => ProviderPriority(d.Source)))
            {
                PeripheralStatus duplicate = output.FirstOrDefault(d => IsSamePhysicalDevice(d, item));
                if (duplicate == null)
                {
                    output.Add(item);
                }
                else if (ProviderPriority(item.Source) >= ProviderPriority(duplicate.Source))
                {
                    int index = output.IndexOf(duplicate);
                    if (!item.HasBattery && duplicate.HasBattery)
                    {
                        item.BatteryPercent = duplicate.BatteryPercent;
                        item.BatteryLevelText = duplicate.BatteryLevelText;
                        item.IsCharging = duplicate.IsCharging;
                    }
                    output[index] = item;
                }
            }

            MergeXInputInterfaces(output);

            foreach (PeripheralStatus item in output)
            {
                if (item.HasBattery) continue;
                PeripheralStatus old = previous.FirstOrDefault(d => IsSamePhysicalDevice(d, item) && d.HasBattery);
                if (old != null && DateTime.Now - old.LastUpdated < TimeSpan.FromMinutes(30))
                {
                    item.BatteryPercent = old.BatteryPercent;
                    item.BatteryLevelText = old.BatteryLevelText;
                    item.IsCharging = old.IsCharging;
                    item.IsSleeping = true;
                    item.LastUpdated = old.LastUpdated;
                }
            }

            return output.OrderBy(d => KindOrder(d.Kind)).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void MergeXInputInterfaces(List<PeripheralStatus> output)
        {
            List<PeripheralStatus> xinput = output.Where(d => d.Source == "Windows XInput").ToList();
            List<PeripheralStatus> compatibleHid = output.Where(d => d.Source == "通用 HID" && d.Kind == PeripheralKind.Gamepad && d.IsXInputCompatible).ToList();
            if (xinput.Count == 1 && compatibleHid.Count == 1)
            {
                PeripheralStatus hid = compatibleHid[0];
                PeripheralStatus x = xinput[0];
                // The HID node knows the actual product and physical container;
                // XInput is only a second API for the same controller.
                if (x.HasBattery)
                {
                    hid.BatteryPercent = x.BatteryPercent;
                    hid.BatteryLevelText = x.BatteryLevelText;
                    hid.IsCharging = x.IsCharging;
                }
                if (x.Connection == ConnectionKind.Wireless24) hid.Connection = x.Connection;
                else if (hid.Connection == ConnectionKind.Usb) hid.Connection = ConnectionKind.Unknown;
                output.Remove(x);
            }
            else if (xinput.Count > 0 && compatibleHid.Count > 0)
            {
                // XInput indices have no physical identity. With several pads,
                // never attach an index's battery to a guessed HID container.
                // Prefer the side that accounts for more distinct controllers.
                if (compatibleHid.Count >= xinput.Count)
                {
                    foreach (PeripheralStatus x in xinput) output.Remove(x);
                }
                else
                {
                    foreach (PeripheralStatus hid in compatibleHid) output.Remove(hid);
                }
            }
        }

        private static bool IsSamePhysicalDevice(PeripheralStatus left, PeripheralStatus right)
        {
            if (String.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)) return true;
            if (left.Kind != right.Kind || String.IsNullOrEmpty(left.PhysicalId) || String.IsNullOrEmpty(right.PhysicalId)) return false;
            if (String.Equals(left.PhysicalId, right.PhysicalId, StringComparison.OrdinalIgnoreCase)) return true;
            // A receiver's generic HID node can represent a Logitech HID++ slot.
            bool receiverProxy = (left.Source == "通用 HID" && right.Source == "Logitech HID++") ||
                                 (right.Source == "通用 HID" && left.Source == "Logitech HID++");
            if (receiverProxy)
            {
                PeripheralStatus generic = left.Source == "通用 HID" ? left : right;
                PeripheralStatus specific = left.Source == "Logitech HID++" ? left : right;
                return specific.PhysicalId.StartsWith(generic.PhysicalId + ":slot:", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static int ProviderPriority(string source)
        {
            if (source == "Logitech HID++") return 100;
            if (source == "Windows XInput") return 90;
            if (source == "Windows 无线设备") return 80;
            return 10;
        }

        private static int KindOrder(PeripheralKind kind)
        {
            switch (kind)
            {
                case PeripheralKind.Mouse: return 0;
                case PeripheralKind.Keyboard: return 1;
                case PeripheralKind.Gamepad: return 2;
                case PeripheralKind.Audio: return 3;
                default: return 4;
            }
        }

        private void RaiseChanged()
        {
            EventHandler handler = DevicesChanged;
            if (handler == null) return;
            if (_context != null) _context.Post(delegate { handler(this, EventArgs.Empty); }, null);
            else handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _disposed = true;
            if (_timer != null) _timer.Dispose();
            if (_topologyTimer != null) _topologyTimer.Dispose();
        }
    }
}
