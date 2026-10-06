using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralPeek.Native
{
    // Read-only access to the Bluetooth SIG Battery Service (180F / 2A19).
    // No pairing, scanning, writes or background BLE connections are started.
    internal static class BluetoothGattNative
    {
        private static readonly Guid ServiceInterface = new Guid("6E3BB679-4372-40C8-9EAA-4509DF260CD8");
        private static readonly Guid ContainerProperty = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C");
        private static readonly Guid BatteryCharacteristic = new Guid("00002A19-0000-1000-8000-00805F9B34FB");
        private const string BatteryServiceId = "{0000180F-0000-1000-8000-00805F9B34FB}";
        private const uint DigcfPresent = 2;
        private const uint DigcfDeviceInterface = 16;

        [StructLayout(LayoutKind.Sequential)]
        private struct InterfaceData
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceData
        {
            public uint Size;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;
        }

        // BTH_LE_UUID contains a one-byte BOOLEAN and a 16-byte union at +4.
        [StructLayout(LayoutKind.Explicit, Size = 20)]
        private struct LeUuid
        {
            [FieldOffset(0)] public byte IsShort;
            [FieldOffset(4)] public ushort Short;
            [FieldOffset(4)] public Guid Long;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Characteristic
        {
            public ushort ServiceHandle;
            public LeUuid Uuid;
            public ushort AttributeHandle;
            public ushort ValueHandle;
            public byte IsBroadcastable;
            public byte IsReadable;
            public byte IsWritable;
            public byte IsWritableWithoutResponse;
            public byte IsSignedWritable;
            public byte IsNotifiable;
            public byte IsIndicatable;
            public byte HasExtendedProperties;
        }

        public static IDictionary<Guid, int> ReadBatteryLevels(ICollection<Guid> connectedContainers)
        {
            Dictionary<Guid, int> levels = new Dictionary<Guid, int>();
            if (connectedContainers == null || connectedContainers.Count == 0) return levels;
            HashSet<Guid> wanted = new HashSet<Guid>(connectedContainers);
            Guid classGuid = ServiceInterface;
            IntPtr set = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == new IntPtr(-1)) return levels;
            try
            {
                for (uint index = 0; ; index++)
                {
                    InterfaceData entry = new InterfaceData();
                    entry.Size = Marshal.SizeOf(typeof(InterfaceData));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref classGuid, index, ref entry)) break;
                    uint needed;
                    SetupDiGetDeviceInterfaceDetail(set, ref entry, IntPtr.Zero, 0, out needed, IntPtr.Zero);
                    if (needed < 8) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        DeviceData node = new DeviceData();
                        node.Size = (uint)Marshal.SizeOf(typeof(DeviceData));
                        if (!SetupDiGetDeviceInterfaceDetailWithNode(set, ref entry, detail, needed, out needed, ref node)) continue;
                        Guid container = ReadContainer(set, ref node);
                        if (!wanted.Contains(container) || levels.ContainsKey(container)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (String.IsNullOrEmpty(path) || path.IndexOf(BatteryServiceId, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        int? battery = ReadBattery(path);
                        if (battery.HasValue) levels[container] = battery.Value;
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return levels;
        }

        internal static int UuidSize { get { return Marshal.SizeOf(typeof(LeUuid)); } }
        internal static int CharacteristicSize { get { return Marshal.SizeOf(typeof(Characteristic)); } }

        private static Guid ReadContainer(IntPtr set, ref DeviceData node)
        {
            PropertyKey key = new PropertyKey { FormatId = ContainerProperty, PropertyId = 2 };
            byte[] data = new byte[16];
            uint type, needed;
            if (SetupDiGetDeviceProperty(set, ref node, ref key, out type, data, (uint)data.Length, out needed, 0) &&
                type == 0x0D && needed == 16)
                return new Guid(data);
            return Guid.Empty;
        }

        private static int? ReadBattery(string path)
        {
            using (SafeFileHandle service = HidNative.CreateFile(path, HidNative.GenericRead, System.IO.FileShare.ReadWrite,
                IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero))
            {
                if (service.IsInvalid) return null;
                ushort count;
                BluetoothGATTGetCharacteristics(service, IntPtr.Zero, 0, IntPtr.Zero, out count, 0);
                int size = CharacteristicSize;
                if (count == 0 || count > 64 || size != 36) return null;
                IntPtr buffer = Marshal.AllocHGlobal(count * size);
                try
                {
                    int result = BluetoothGATTGetCharacteristics(service, IntPtr.Zero, count, buffer, out count, 0);
                    if (result != 0 || count > 64) return null;
                    for (int i = 0; i < count; i++)
                    {
                        Characteristic item = (Characteristic)Marshal.PtrToStructure(IntPtr.Add(buffer, i * size), typeof(Characteristic));
                        if (item.Uuid.IsShort != 0 ? item.Uuid.Short != 0x2A19 : item.Uuid.Long != BatteryCharacteristic) continue;
                        if (item.IsReadable == 0) continue;
                        ushort needed;
                        BluetoothGATTGetCharacteristicValue(service, ref item, 0, IntPtr.Zero, out needed, 0);
                        if (needed < 5 || needed > 64) continue;
                        ushort capacity = needed;
                        IntPtr value = Marshal.AllocHGlobal(capacity);
                        try
                        {
                            int read = BluetoothGATTGetCharacteristicValue(service, ref item, capacity, value, out needed, 4); // FORCE_READ_FROM_DEVICE
                            if (read != 0) read = BluetoothGATTGetCharacteristicValue(service, ref item, capacity, value, out needed, 0);
                            int dataSize = read == 0 ? Marshal.ReadInt32(value) : 0;
                            if (dataSize < 1 || dataSize > capacity - 4) continue;
                            int percent = Marshal.ReadByte(value, 4);
                            if (percent <= 100) return percent;
                        }
                        finally { Marshal.FreeHGlobal(value); }
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return null;
        }

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, ref Guid classGuid, uint index, ref InterfaceData entry);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData entry, IntPtr detail, uint size, out uint needed, IntPtr node);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetailWithNode(IntPtr set, ref InterfaceData entry, IntPtr detail, uint size, out uint needed, ref DeviceData node);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
        private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DeviceData node, ref PropertyKey key, out uint type, byte[] value, uint size, out uint needed, uint flags);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("BluetoothAPIs.dll", ExactSpelling = true)]
        private static extern int BluetoothGATTGetCharacteristics(SafeFileHandle service, IntPtr parent, ushort capacity, IntPtr buffer, out ushort count, uint flags);
        [DllImport("BluetoothAPIs.dll", ExactSpelling = true)]
        private static extern int BluetoothGATTGetCharacteristicValue(SafeFileHandle service, ref Characteristic item, uint capacity, IntPtr value, out ushort needed, uint flags);
    }
}
