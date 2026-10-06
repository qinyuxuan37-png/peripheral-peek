using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PeripheralPeek.Native
{
    internal static class BluetoothNative
    {
        public static IDictionary<string, bool> GetKnownDeviceStates()
        {
            Dictionary<string, bool> states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            SearchParams search = new SearchParams();
            search.Size = (uint)Marshal.SizeOf(typeof(SearchParams));
            search.ReturnAuthenticated = 1;
            search.ReturnRemembered = 1;
            search.ReturnConnected = 1;
            DeviceInfo info = new DeviceInfo();
            info.Size = (uint)Marshal.SizeOf(typeof(DeviceInfo));
            IntPtr finder = BluetoothFindFirstDevice(ref search, ref info);
            if (finder == IntPtr.Zero) return states;
            try
            {
                do
                {
                    states[(info.Address & 0xFFFFFFFFFFFFUL).ToString("X12")] = info.Connected != 0;
                    info.Size = (uint)Marshal.SizeOf(typeof(DeviceInfo));
                }
                while (BluetoothFindNextDevice(finder, ref info));
            }
            finally { BluetoothFindDeviceClose(finder); }
            return states;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SearchParams
        {
            public uint Size;
            public int ReturnAuthenticated;
            public int ReturnRemembered;
            public int ReturnUnknown;
            public int ReturnConnected;
            public int IssueInquiry;
            public byte TimeoutMultiplier;
            public IntPtr Radio;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTime
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DeviceInfo
        {
            public uint Size;
            public ulong Address;
            public uint ClassOfDevice;
            public int Connected;
            public int Remembered;
            public int Authenticated;
            public SystemTime LastSeen;
            public SystemTime LastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string Name;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstDevice(ref SearchParams search, ref DeviceInfo info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextDevice(IntPtr finder, ref DeviceInfo info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindDeviceClose(IntPtr finder);
    }
}
