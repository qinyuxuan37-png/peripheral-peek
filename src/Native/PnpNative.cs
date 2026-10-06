using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PeripheralPeek.Native
{
    internal sealed class PnpDeviceInfo
    {
        public string InstanceId;
        public Guid ContainerId;
        public string ClassName;
        public string FriendlyName;
        public string BusName;
        public int? BatteryPercent;
        public bool? IsCharging;
        public bool? IsConnected;
    }

    internal static class PnpNative
    {
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfAllClasses = 0x00000004;
        private const uint DevpropTypeString = 0x00000012;
        private const uint DevpropTypeGuid = 0x0000000D;
        private const uint DevpropTypeByte = 0x00000003;
        private const uint DevpropTypeBoolean = 0x00000011;

        private static readonly Devpropkey ContainerId = Key("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2);
        private static readonly Devpropkey FriendlyName = Key("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);
        private static readonly Devpropkey DeviceDescription = Key("A45C254E-DF1C-4EFD-8020-67D146A850E0", 2);
        private static readonly Devpropkey ClassName = Key("A45C254E-DF1C-4EFD-8020-67D146A850E0", 9);
        private static readonly Devpropkey BusName = Key("540B947E-8B40-45BC-A8A2-6A0B894CBDA2", 4);
        private static readonly Devpropkey BatteryLife = Key("49CD1F76-5626-4B17-A4E8-18B4AA1A2213", 10);
        // Windows Bluetooth stack battery value; exposed on some headset service nodes.
        private static readonly Devpropkey BluetoothBatteryLevel = Key("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
        private static readonly Devpropkey BatteryPlusCharging = Key("49CD1F76-5626-4B17-A4E8-18B4AA1A2213", 22);
        private static readonly Devpropkey AepIsConnected = Key("A35996AB-11CF-4935-8B61-A6761081ECDF", 7);

        public static IList<PnpDeviceInfo> EnumeratePresent()
        {
            List<PnpDeviceInfo> results = new List<PnpDeviceInfo>();
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (set == new IntPtr(-1)) return results;
            try
            {
                for (uint index = 0; ; index++)
                {
                    SpDevinfoData data = new SpDevinfoData();
                    data.Size = (uint)Marshal.SizeOf(typeof(SpDevinfoData));
                    if (!SetupDiEnumDeviceInfo(set, index, ref data)) break;
                    StringBuilder id = new StringBuilder(1024);
                    uint required;
                    if (!SetupDiGetDeviceInstanceId(set, ref data, id, (uint)id.Capacity, out required)) continue;
                    byte[] container = ReadProperty(set, ref data, ContainerId, DevpropTypeGuid);
                    if (container == null || container.Length < 16) continue;
                    PnpDeviceInfo device = new PnpDeviceInfo();
                    device.InstanceId = id.ToString();
                    byte[] guidBytes = new byte[16];
                    Array.Copy(container, guidBytes, 16);
                    device.ContainerId = new Guid(guidBytes);
                    device.ClassName = ReadString(set, ref data, ClassName);
                    device.FriendlyName = ReadString(set, ref data, FriendlyName);
                    if (String.IsNullOrWhiteSpace(device.FriendlyName)) device.FriendlyName = ReadString(set, ref data, DeviceDescription);
                    device.BusName = ReadString(set, ref data, BusName);
                    device.BatteryPercent = ReadPercent(set, ref data, BluetoothBatteryLevel) ??
                                            ReadPercent(set, ref data, BatteryLife);
                    int? combinedBattery = ReadByte(set, ref data, BatteryPlusCharging);
                    if (combinedBattery.HasValue && combinedBattery.Value <= 200)
                    {
                        if (combinedBattery.Value >= 101)
                        {
                            device.IsCharging = true;
                            if (!device.BatteryPercent.HasValue) device.BatteryPercent = combinedBattery.Value - 100;
                        }
                        else if (!device.BatteryPercent.HasValue) device.BatteryPercent = combinedBattery;
                    }
                    byte[] connected = ReadProperty(set, ref data, AepIsConnected, DevpropTypeBoolean);
                    if (connected != null && connected.Length > 0) device.IsConnected = connected[0] != 0;
                    results.Add(device);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return results;
        }

        private static Devpropkey Key(string guid, uint id)
        {
            Devpropkey key = new Devpropkey();
            key.FormatId = new Guid(guid);
            key.PropertyId = id;
            return key;
        }

        private static string ReadString(IntPtr set, ref SpDevinfoData data, Devpropkey key)
        {
            byte[] bytes = ReadProperty(set, ref data, key, DevpropTypeString);
            return bytes == null ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
        }

        private static int? ReadByte(IntPtr set, ref SpDevinfoData data, Devpropkey key)
        {
            byte[] bytes = ReadProperty(set, ref data, key, DevpropTypeByte);
            return bytes == null || bytes.Length == 0 ? (int?)null : bytes[0];
        }

        private static int? ReadPercent(IntPtr set, ref SpDevinfoData data, Devpropkey key)
        {
            int? value = ReadByte(set, ref data, key);
            return value.HasValue && value.Value <= 100 ? value : null;
        }

        private static byte[] ReadProperty(IntPtr set, ref SpDevinfoData data, Devpropkey key, uint expectedType)
        {
            byte[] buffer = new byte[1024];
            uint type;
            uint required;
            if (!SetupDiGetDeviceProperty(set, ref data, ref key, out type, buffer, (uint)buffer.Length, out required, 0)) return null;
            if (type != expectedType || required > buffer.Length) return null;
            byte[] value = new byte[required];
            Array.Copy(buffer, value, required);
            return value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDevinfoData
        {
            public uint Size;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Devpropkey
        {
            public Guid FormatId;
            public uint PropertyId;
        }

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevinfoData data);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SpDevinfoData data, StringBuilder id, uint capacity, out uint required);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
        private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SpDevinfoData data, ref Devpropkey key, out uint type, byte[] buffer, uint bufferSize, out uint required, uint flags);

        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}
