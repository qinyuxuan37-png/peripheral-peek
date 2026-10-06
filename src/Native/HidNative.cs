using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralPeek.Native
{
    internal sealed class HidInfo
    {
        public string Path;
        public string Product;
        public string Manufacturer;
        public string Serial;
        public Guid ContainerId;
        public int VendorId;
        public int ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public int InputReportLength;
        public int OutputReportLength;
        public int FeatureReportLength;
    }

    internal static class HidNative
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct OverlappedData
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr EventHandle;
        }

        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileFlagOverlapped = 0x40000000;
        internal const uint OpenExisting = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct HiddAttributes
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetailWithNode(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, ref SpDevinfoData deviceInfoData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
        private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SpDevinfoData data, ref Devpropkey key, out uint type, byte[] buffer, uint bufferSize, out uint required, uint flags);

        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, uint bytesToWrite, out uint bytesWritten, ref OverlappedData overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, uint bytesToRead, out uint bytesRead, ref OverlappedData overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetOverlappedResult(SafeFileHandle handle, ref OverlappedData overlapped, out uint transferred, bool wait);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle deviceObject, ref HiddAttributes attributes);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetProductString(SafeFileHandle deviceObject, IntPtr buffer, int bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetManufacturerString(SafeFileHandle deviceObject, IntPtr buffer, int bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetSerialNumberString(SafeFileHandle deviceObject, IntPtr buffer, int bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle deviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

        public static IList<HidInfo> Enumerate()
        {
            List<HidInfo> results = new List<HidInfo>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == new IntPtr(-1)) return results;

            try
            {
                uint index = 0;
                while (true)
                {
                    SpDeviceInterfaceData data = new SpDeviceInterfaceData();
                    data.cbSize = Marshal.SizeOf(typeof(SpDeviceInterfaceData));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data)) break;
                    index++;

                    uint required;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required == 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        SpDevinfoData node = new SpDevinfoData();
                        node.Size = (uint)Marshal.SizeOf(typeof(SpDevinfoData));
                        if (!SetupDiGetDeviceInterfaceDetailWithNode(set, ref data, detail, required, out required, ref node)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        HidInfo info = ReadInfo(path);
                        if (info != null)
                        {
                            info.ContainerId = ReadContainerId(set, ref node);
                            results.Add(info);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return results;
        }

        private static Guid ReadContainerId(IntPtr set, ref SpDevinfoData node)
        {
            Devpropkey key = new Devpropkey();
            key.FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C");
            key.PropertyId = 2;
            byte[] bytes = new byte[16];
            uint type;
            uint required;
            if (!SetupDiGetDeviceProperty(set, ref node, ref key, out type, bytes, 16, out required, 0) || type != 0x0D || required != 16) return Guid.Empty;
            return new Guid(bytes);
        }

        private static HidInfo ReadInfo(string path)
        {
            using (SafeFileHandle handle = CreateFile(path, 0, FileShare.ReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return null;
                HiddAttributes attributes = new HiddAttributes();
                attributes.Size = Marshal.SizeOf(typeof(HiddAttributes));
                if (!HidD_GetAttributes(handle, ref attributes)) return null;

                HidInfo info = new HidInfo();
                info.Path = path;
                info.VendorId = attributes.VendorID;
                info.ProductId = attributes.ProductID;
                info.Product = ReadHidString(handle, HidD_GetProductString);
                info.Manufacturer = ReadHidString(handle, HidD_GetManufacturerString);
                info.Serial = ReadHidString(handle, HidD_GetSerialNumberString);

                IntPtr preparsed;
                if (HidD_GetPreparsedData(handle, out preparsed))
                {
                    try
                    {
                        HidpCaps caps;
                        if (HidP_GetCaps(preparsed, out caps) >= 0)
                        {
                            info.Usage = caps.Usage;
                            info.UsagePage = caps.UsagePage;
                            info.InputReportLength = caps.InputReportByteLength;
                            info.OutputReportLength = caps.OutputReportByteLength;
                            info.FeatureReportLength = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        HidD_FreePreparsedData(preparsed);
                    }
                }
                return info;
            }
        }

        private delegate bool HidStringReader(SafeFileHandle handle, IntPtr buffer, int length);

        private static string ReadHidString(SafeFileHandle handle, HidStringReader reader)
        {
            IntPtr buffer = Marshal.AllocHGlobal(512);
            try
            {
                for (int i = 0; i < 512; i++) Marshal.WriteByte(buffer, i, 0);
                if (!reader(handle, buffer, 512)) return null;
                return Marshal.PtrToStringUni(buffer);
            }
            catch { return null; }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
