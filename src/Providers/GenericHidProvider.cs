using System;
using System.Collections.Generic;
using System.Linq;
using PeripheralPeek.Native;

namespace PeripheralPeek.Providers
{
    internal sealed class GenericHidProvider : ISharedScanProvider, ICacheInvalidatable
    {
        private readonly object _cacheGate = new object();
        private HashSet<Guid> _xinputContainers = new HashSet<Guid>();
        private DateTime _xinputCacheExpiresUtc = DateTime.MinValue;

        public string Name { get { return "通用 HID"; } }

        public void InvalidateCache()
        {
            lock (_cacheGate) _xinputCacheExpiresUtc = DateTime.MinValue;
        }

        private HashSet<Guid> GetXInputContainers(DeviceScanSnapshot snapshot)
        {
            lock (_cacheGate)
            {
                if (DateTime.UtcNow < _xinputCacheExpiresUtc) return _xinputContainers;
                _xinputContainers = new HashSet<Guid>(snapshot.Pnp
                    .Where(p => p.ContainerId != Guid.Empty && String.Equals(p.ClassName, "XnaComposite", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.ContainerId));
                _xinputCacheExpiresUtc = DateTime.UtcNow.AddSeconds(20);
                return _xinputContainers;
            }
        }

        public IList<PeripheralStatus> Scan()
        {
            return Scan(new DeviceScanSnapshot());
        }

        public IList<PeripheralStatus> Scan(DeviceScanSnapshot snapshot)
        {
            Dictionary<string, PeripheralStatus> devices = new Dictionary<string, PeripheralStatus>(StringComparer.OrdinalIgnoreCase);
            try
            {
                IList<HidInfo> interfaces = snapshot.Hids;
                HashSet<Guid> xinputContainers = null;
                if (interfaces.Any(h => Classify(h) == PeripheralKind.Gamepad))
                    xinputContainers = GetXInputContainers(snapshot);
                foreach (HidInfo hid in interfaces)
                {
                    PeripheralKind? kind = Classify(hid);
                    if (!kind.HasValue) continue;
                    if (kind.Value == PeripheralKind.Gamepad && hid.VendorId == 0x045E) continue;
                    if (hid.VendorId == 0x046D && hid.ProductId == 0xC232) continue;
                    if (IsVirtual(hid)) continue;

                    string product = CleanName(hid.Product, kind.Value);
                    string identity = hid.ContainerId != Guid.Empty ? hid.ContainerId.ToString("N") :
                        (!String.IsNullOrEmpty(hid.Serial) ? hid.Serial : hid.Path);
                    string id = String.Format("hid:{0:x4}:{1:x4}:{2}:{3}", hid.VendorId, hid.ProductId, Normalize(identity), kind.Value);
                    if (devices.ContainsKey(id)) continue;

                    devices[id] = new PeripheralStatus
                    {
                        Id = id,
                        PhysicalId = hid.ContainerId == Guid.Empty ? null : "container:" + hid.ContainerId.ToString("N"),
                        Name = product,
                        Kind = kind.Value,
                        Connection = DetectConnection(hid),
                        LastUpdated = DateTime.Now,
                        Source = Name,
                        IsXInputCompatible = kind.Value == PeripheralKind.Gamepad &&
                            ((!String.IsNullOrEmpty(hid.Path) && hid.Path.IndexOf("ig_", StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (xinputContainers != null && xinputContainers.Contains(hid.ContainerId))),
                        VendorId = hid.VendorId,
                        ProductId = hid.ProductId
                    };
                }

                RemoveCompositeDuplicates(devices);
            }
            catch (Exception ex)
            {
                ProviderLog.Write(Name, ex);
            }
            return new List<PeripheralStatus>(devices.Values);
        }

        internal static void RemoveCompositeDuplicatesForTest(List<PeripheralStatus> input)
        {
            Dictionary<string, PeripheralStatus> devices = new Dictionary<string, PeripheralStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (PeripheralStatus item in input) devices[item.Id] = item;
            RemoveCompositeDuplicates(devices);
            input.RemoveAll(d => !devices.ContainsKey(d.Id));
        }

        private static void RemoveCompositeDuplicates(Dictionary<string, PeripheralStatus> devices)
        {
            List<PeripheralStatus> values = new List<PeripheralStatus>(devices.Values);
            // A composite controller may expose mouse and keyboard usages in
            // addition to its gamepad interface. Container + VID/PID identifies
            // these as parts of the same device, regardless of brand or model.
            foreach (PeripheralStatus auxiliary in values)
            {
                if ((auxiliary.Kind != PeripheralKind.Mouse && auxiliary.Kind != PeripheralKind.Keyboard) || String.IsNullOrEmpty(auxiliary.PhysicalId)) continue;
                if (values.Exists(d => d.Kind == PeripheralKind.Gamepad &&
                    d.VendorId == auxiliary.VendorId && d.ProductId == auxiliary.ProductId &&
                    String.Equals(d.PhysicalId, auxiliary.PhysicalId, StringComparison.OrdinalIgnoreCase)))
                    devices.Remove(auxiliary.Id);
            }
            foreach (PeripheralStatus mouse in values)
            {
                if (!devices.ContainsKey(mouse.Id)) continue;
                if (mouse.Kind != PeripheralKind.Mouse) continue;
                PeripheralStatus keyboard = values.Find(d => d.Kind == PeripheralKind.Keyboard && d.VendorId == mouse.VendorId && d.ProductId == mouse.ProductId &&
                    !String.IsNullOrEmpty(d.PhysicalId) && String.Equals(d.PhysicalId, mouse.PhysicalId, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(d.Name, mouse.Name, StringComparison.OrdinalIgnoreCase));
                if (keyboard == null) continue;

                bool logitechReceiver = mouse.VendorId == 0x046D && mouse.ProductId >= 0xC500;
                bool explicitlyMouse = mouse.Name.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0 || mouse.Name.IndexOf("鼠标", StringComparison.OrdinalIgnoreCase) >= 0;
                PeripheralStatus remove = logitechReceiver || explicitlyMouse ? keyboard : mouse;
                devices.Remove(remove.Id);
            }
        }

        private static PeripheralKind? Classify(HidInfo hid)
        {
            if (hid.UsagePage != 0x01) return null;
            if (hid.Usage == 0x02 || hid.Usage == 0x01) return PeripheralKind.Mouse;
            if (hid.Usage == 0x06) return PeripheralKind.Keyboard;
            if (hid.Usage == 0x04 || hid.Usage == 0x05) return PeripheralKind.Gamepad;
            return null;
        }

        private static bool IsVirtual(HidInfo hid)
        {
            string text = ((hid.Product ?? "") + " " + (hid.Manufacturer ?? "") + " " + hid.Path).ToLowerInvariant();
            return text.Contains("virtual") || text.Contains("lghubdevice") || text.Contains("root#rdp") || text.Contains("vigem");
        }

        private static ConnectionKind DetectConnection(HidInfo hid)
        {
            string path = (hid.Path ?? String.Empty).ToLowerInvariant();
            string description = ((hid.Product ?? String.Empty) + " " + (hid.Manufacturer ?? String.Empty) + " " + path).ToLowerInvariant();
            if (description.Contains("bthenum") || description.Contains("bthledevice") || description.Contains("bluetooth") || description.Contains("蓝牙")) return ConnectionKind.Bluetooth;
            if (description.Contains("receiver") || description.Contains("dongle") || description.Contains("wireless") ||
                description.Contains("2.4g") || description.Contains("2.4 g") || description.Contains("2.4ghz") ||
                description.Contains("接收器") || description.Contains("无线")) return ConnectionKind.Wireless24;
            return ConnectionKind.Usb;
        }

        private static string CleanName(string product, PeripheralKind kind)
        {
            if (!String.IsNullOrWhiteSpace(product))
            {
                string name = product.Trim();
                if (kind == PeripheralKind.Gamepad && name.StartsWith("Controller (", StringComparison.OrdinalIgnoreCase) && name.EndsWith(")", StringComparison.Ordinal))
                    return name.Substring("Controller (".Length, name.Length - "Controller (".Length - 1);
                return name;
            }
            switch (kind)
            {
                case PeripheralKind.Mouse: return "鼠标";
                case PeripheralKind.Keyboard: return "键盘";
                case PeripheralKind.Gamepad: return "游戏控制器";
                default: return "外设";
            }
        }

        internal static string Normalize(string value)
        {
            if (String.IsNullOrEmpty(value)) return "device";
            char[] chars = value.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!Char.IsLetterOrDigit(chars[i])) chars[i] = '-';
            }
            return new string(chars).Trim('-');
        }
    }
}
