using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PeripheralPeek.Native;

namespace PeripheralPeek.Providers
{
    internal sealed class BluetoothProvider : ISharedScanProvider, ICacheInvalidatable
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
        private readonly object _cacheGate = new object();
        private List<PeripheralStatus> _cache = new List<PeripheralStatus>();
        private DateTime _cacheExpiresUtc = DateTime.MinValue;

        public string Name { get { return "Windows 无线设备"; } }

        public void InvalidateCache()
        {
            lock (_cacheGate) _cacheExpiresUtc = DateTime.MinValue;
        }

        public IList<PeripheralStatus> Scan()
        {
            return Scan(new DeviceScanSnapshot());
        }

        public IList<PeripheralStatus> Scan(DeviceScanSnapshot snapshot)
        {
            lock (_cacheGate)
            {
                if (DateTime.UtcNow < _cacheExpiresUtc) return CloneList(_cache);
            }

            List<PeripheralStatus> result = new List<PeripheralStatus>();
            Dictionary<Guid, PeripheralStatus> leBatteryCandidates = new Dictionary<Guid, PeripheralStatus>();
            try
            {
                IDictionary<string, bool> bluetoothStates = BluetoothNative.GetKnownDeviceStates();
                foreach (IGrouping<Guid, PnpDeviceInfo> group in snapshot.Pnp.GroupBy(d => d.ContainerId))
                {
                    if (group.Key == Guid.Empty || group.Key == new Guid("00000000-0000-0000-ffff-ffffffffffff")) continue;
                    List<PnpDeviceInfo> members = group.ToList();
                    bool bluetooth = members.Any(d => IsBluetoothId(d.InstanceId));
                    bool usb = members.Any(d => d.InstanceId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase));
                    string allNames = String.Join(" ", members.Select(d => (d.FriendlyName ?? "") + " " + (d.BusName ?? "")));
                    if (!bluetooth && !(usb && IsWirelessName(allNames))) continue;

                    if (bluetooth)
                    {
                        bool connected = members.Any(d => d.IsConnected == true);
                        bool classicConnected = members.Any(d =>
                        {
                            Match address = Regex.Match(d.InstanceId, @"^BTHENUM\\DEV_([0-9A-F]{12})", RegexOptions.IgnoreCase);
                            bool isConnected;
                            return address.Success && bluetoothStates.TryGetValue(address.Groups[1].Value, out isConnected) && isConnected;
                        });
                        if (!connected && !classicConnected) continue;
                    }

                    PeripheralKind kind = Classify(members, allNames, bluetooth);
                    PnpDeviceInfo battery = members.FirstOrDefault(d => d.BatteryPercent.HasValue);
                    if (!bluetooth && kind != PeripheralKind.Audio && battery == null) continue;

                    PnpDeviceInfo usbRoot = members.FirstOrDefault(d => d.InstanceId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase));
                    PnpDeviceInfo bluetoothRoot = members.FirstOrDefault(d => Regex.IsMatch(d.InstanceId, @"^BTH(ENUM|LEDEVICE)\\DEV_", RegexOptions.IgnoreCase));
                    string name = PickName(members, bluetooth ? bluetoothRoot : usbRoot, bluetooth, kind);
                    Match idMatch = Regex.Match(usbRoot == null ? String.Empty : usbRoot.InstanceId, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                    int vendorId = idMatch.Success ? Convert.ToInt32(idMatch.Groups[1].Value, 16) : 0;
                    int productId = idMatch.Success ? Convert.ToInt32(idMatch.Groups[2].Value, 16) : 0;

                    PeripheralStatus status = new PeripheralStatus
                    {
                        Id = "wireless:" + group.Key.ToString("N") + ":" + kind,
                        PhysicalId = "container:" + group.Key.ToString("N"),
                        Name = name,
                        Kind = kind,
                        Connection = bluetooth ? ConnectionKind.Bluetooth : ConnectionKind.Wireless24,
                        BatteryPercent = battery == null ? null : battery.BatteryPercent,
                        IsCharging = battery == null ? null : battery.IsCharging,
                        LastUpdated = DateTime.Now,
                        Source = Name,
                        VendorId = vendorId,
                        ProductId = productId
                    };
                    result.Add(status);
                    if (bluetooth && !status.BatteryPercent.HasValue &&
                        members.Any(d => d.InstanceId.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase)))
                        leBatteryCandidates[group.Key] = status;
                }

                if (leBatteryCandidates.Count > 0)
                {
                    try
                    {
                        IDictionary<Guid, int> levels = BluetoothGattNative.ReadBatteryLevels(leBatteryCandidates.Keys.ToList());
                        foreach (KeyValuePair<Guid, int> level in levels)
                            leBatteryCandidates[level.Key].BatteryPercent = level.Value;
                    }
                    catch (Exception ex) { ProviderLog.Write("蓝牙 LE 电量", ex); }
                }
            }
            catch (Exception ex) { ProviderLog.Write(Name, ex); }

            lock (_cacheGate)
            {
                _cache = CloneList(result);
                _cacheExpiresUtc = DateTime.UtcNow.Add(CacheDuration);
            }
            return result;
        }

        private static PeripheralKind Classify(IList<PnpDeviceInfo> members, string names, bool bluetooth)
        {
            if (ContainsAny(names, "controller", "gamepad", "joystick", "手柄")) return PeripheralKind.Gamepad;
            if (members.Any(d => EqualClass(d.ClassName, "Mouse")) || ContainsAny(names, "mouse", "鼠标")) return PeripheralKind.Mouse;
            if (members.Any(d => EqualClass(d.ClassName, "Keyboard")) || ContainsAny(names, "keyboard", "keypad", "键盘")) return PeripheralKind.Keyboard;
            if (members.Any(d => EqualClass(d.ClassName, "AudioEndpoint") || EqualClass(d.ClassName, "MEDIA")) ||
                ContainsAny(names, "headphone", "headset", "speaker", "audio", "sound", "earbud", "耳机", "音响", "音箱")) return PeripheralKind.Audio;
            return bluetooth ? PeripheralKind.Audio : PeripheralKind.Other;
        }

        private static string PickName(IList<PnpDeviceInfo> members, PnpDeviceInfo root, bool bluetooth, PeripheralKind kind)
        {
            string name = null;
            if (bluetooth && root != null) name = root.FriendlyName;
            if (!bluetooth)
            {
                PnpDeviceInfo endpoint = members.FirstOrDefault(d => EqualClass(d.ClassName, "AudioEndpoint"));
                if (endpoint != null) name = endpoint.FriendlyName;
                if (String.IsNullOrWhiteSpace(name) && root != null) name = String.IsNullOrWhiteSpace(root.BusName) ? root.FriendlyName : root.BusName;
            }
            if (String.IsNullOrWhiteSpace(name))
                name = members.SelectMany(d => new[] { d.BusName, d.FriendlyName }).FirstOrDefault(n => !String.IsNullOrWhiteSpace(n) && !IsGenericName(n));
            if (String.IsNullOrWhiteSpace(name)) name = kind == PeripheralKind.Audio ? "无线音频设备" : "无线外设";
            Match parenthesized = Regex.Match(name, @"^.+\((.+)\)$");
            if (parenthesized.Success) name = parenthesized.Groups[1].Value;
            return name.Trim();
        }

        private static bool IsGenericName(string name)
        {
            return ContainsAny(name, "enumerator", "adapter", "枚举器", "适配器", "符合 HID", "HID-compliant", "USB 输入设备");
        }

        private static bool IsWirelessName(string text)
        {
            return ContainsAny(text, "wireless", "receiver", "dongle", "2.4g", "2.4 g", "2.4ghz", "无线", "接收器");
        }

        private static bool IsBluetoothId(string id)
        {
            return id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EqualClass(string left, string right)
        {
            return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            if (String.IsNullOrEmpty(text)) return false;
            foreach (string term in terms)
                if (text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static List<PeripheralStatus> CloneList(IList<PeripheralStatus> source)
        {
            List<PeripheralStatus> copy = new List<PeripheralStatus>(source.Count);
            foreach (PeripheralStatus item in source) copy.Add(item.Clone());
            return copy;
        }
    }
}
