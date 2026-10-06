using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PeripheralPeek.Native;

namespace PeripheralPeek.Providers
{
    internal sealed class LogitechHidppProvider : ISharedScanProvider
    {
        private const int LogitechVendorId = 0x046D;
        private const byte SoftwareId = 0x0D;
        internal static bool TraceEnabled;

        public string Name { get { return "Logitech HID++"; } }

        public IList<PeripheralStatus> Scan()
        {
            return Scan(new DeviceScanSnapshot());
        }

        public IList<PeripheralStatus> Scan(DeviceScanSnapshot snapshot)
        {
            List<PeripheralStatus> results = new List<PeripheralStatus>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                IList<HidInfo> allHids = snapshot.Hids;
                foreach (HidInfo hid in allHids)
                {
                    if (hid.VendorId != LogitechVendorId) continue;
                    if (hid.InputReportLength < 20 || hid.OutputReportLength < 20) continue;
                    if ((hid.UsagePage != 0xFF00 && hid.UsagePage != 0xFF43) || hid.Usage != 0x0002) continue;
                    HidInfo shortHid = null;
                    foreach (HidInfo candidate in allHids)
                    {
                        if (candidate.VendorId == hid.VendorId && candidate.ProductId == hid.ProductId && candidate.UsagePage == hid.UsagePage && candidate.Usage == 0x0001 && candidate.OutputReportLength >= 7 &&
                            (hid.ContainerId == Guid.Empty || candidate.ContainerId == hid.ContainerId))
                        {
                            shortHid = candidate;
                            break;
                        }
                    }
                    if (shortHid == null) continue;

                    foreach (byte deviceIndex in new byte[] { 0x01, 0xFF, 0x02 })
                    {
                        PeripheralStatus status = TryReadDevice(shortHid, hid, deviceIndex);
                        if (status != null && seen.Add(status.Id)) results.Add(status);
                    }
                }
            }
            catch (Exception ex)
            {
                ProviderLog.Write(Name, ex);
            }
            return results;
        }

        private PeripheralStatus TryReadDevice(HidInfo shortHid, HidInfo longHid, byte deviceIndex)
        {
            try
            {
                using (SafeFileHandle handle = HidNative.CreateFile(longHid.Path, HidNative.GenericRead | HidNative.GenericWrite,
                    FileShare.ReadWrite, IntPtr.Zero, HidNative.OpenExisting, HidNative.FileFlagOverlapped, IntPtr.Zero))
                {
                    if (handle.IsInvalid) return null;
                    if (Request(handle, handle, longHid, longHid, deviceIndex, 0, 1, new byte[] { 0, 0, 0x5A }, 2200) == null) return null;
                        int unified = ResolveFeature(handle, handle, longHid, longHid, deviceIndex, 0x1004);
                        int levelStatus = ResolveFeature(handle, handle, longHid, longHid, deviceIndex, 0x1000);
                        int voltageFeature = ResolveFeature(handle, handle, longHid, longHid, deviceIndex, 0x1001);
                        if (unified <= 0 && levelStatus <= 0 && voltageFeature <= 0) return null;

                        int? percent = null;
                        bool? charging = null;
                        if (unified > 0)
                        {
                            byte[] response = Request(handle, handle, longHid, longHid, deviceIndex, (byte)unified, 1, new byte[] { 0, 0, 0 });
                            if (response != null && response.Length > 6 && response[4] <= 100)
                            {
                                percent = response[4];
                                charging = DecodeCharging(response[6]);
                            }
                        }
                        if (!percent.HasValue && levelStatus > 0)
                        {
                            byte[] response = Request(handle, handle, longHid, longHid, deviceIndex, (byte)levelStatus, 0, new byte[] { 0, 0, 0 });
                            if (response != null && response.Length > 6 && response[4] <= 100)
                            {
                                percent = response[4];
                                charging = DecodeCharging(response[6]);
                            }
                        }
                        if (!percent.HasValue && voltageFeature > 0)
                        {
                            byte[] response = Request(handle, handle, longHid, longHid, deviceIndex, (byte)voltageFeature, 0, new byte[] { 0, 0, 0 });
                            if (response != null && response.Length > 6)
                            {
                                int millivolts = (response[4] << 8) | response[5];
                                percent = VoltageToPercent(millivolts);
                                charging = DecodeCharging(response[6]);
                            }
                        }
                        if (!percent.HasValue) return null;

                        string deviceName = ReadDeviceName(handle, handle, longHid, longHid, deviceIndex);
                        if (String.IsNullOrWhiteSpace(deviceName)) deviceName = longHid.Product;
                        if (String.IsNullOrWhiteSpace(deviceName)) deviceName = "Logitech 无线设备";
                        PeripheralKind kind = GuessKind(deviceName);
                        string receiver = longHid.ContainerId == Guid.Empty
                            ? GenericHidProvider.Normalize(longHid.Path)
                            : longHid.ContainerId.ToString("N");
                        string id = String.Format("logitech:{0:x4}:{1:x4}:{2}:{3}", longHid.VendorId, longHid.ProductId, receiver, deviceIndex);
                        return new PeripheralStatus
                        {
                            Id = id,
                            PhysicalId = longHid.ContainerId == Guid.Empty ? null : "container:" + longHid.ContainerId.ToString("N") + ":slot:" + deviceIndex,
                            Name = deviceName.Trim(),
                            Kind = kind,
                            Connection = ConnectionKind.Wireless24,
                            BatteryPercent = percent,
                            IsCharging = charging,
                            LastUpdated = DateTime.Now,
                            Source = Name,
                            VendorId = longHid.VendorId,
                            ProductId = longHid.ProductId
                        };
                }
            }
            catch (Exception ex)
            {
                ProviderLog.Write(Name, ex);
                return null;
            }
        }

        private static int ResolveFeature(SafeFileHandle writeHandle, SafeFileHandle readHandle, HidInfo writeHid, HidInfo readHid, byte deviceIndex, int featureId)
        {
            byte[] parameters = new byte[] { (byte)(featureId >> 8), (byte)featureId, 0 };
            byte[] response = Request(writeHandle, readHandle, writeHid, readHid, deviceIndex, 0, 0, parameters);
            if (response == null || response.Length < 7) return -1;
            int index = response[4];
            return index == 0 ? -1 : index;
        }

        private static string ReadDeviceName(SafeFileHandle writeHandle, SafeFileHandle readHandle, HidInfo writeHid, HidInfo readHid, byte deviceIndex)
        {
            int feature = ResolveFeature(writeHandle, readHandle, writeHid, readHid, deviceIndex, 0x0005);
            if (feature <= 0) return null;
            byte[] countResponse = Request(writeHandle, readHandle, writeHid, readHid, deviceIndex, (byte)feature, 0, new byte[] { 0, 0, 0 });
            if (countResponse == null || countResponse.Length < 5) return null;
            int count = countResponse[4];
            if (count <= 0 || count > 64) return null;
            List<byte> bytes = new List<byte>();
            int offset = 0;
            while (offset < count)
            {
                byte[] response = Request(writeHandle, readHandle, writeHid, readHid, deviceIndex, (byte)feature, 1, new byte[] { (byte)offset, 0, 0 });
                if (response == null || response.Length <= 4) break;
                int available = Math.Min(response.Length - 4, count - offset);
                for (int i = 0; i < available; i++)
                {
                    if (response[4 + i] != 0) bytes.Add(response[4 + i]);
                }
                if (available <= 0) break;
                offset += available;
            }
            return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray()).Trim('\0', ' ');
        }

        private static byte[] Request(SafeFileHandle writeHandle, SafeFileHandle readHandle, HidInfo writeHid, HidInfo readHid, byte deviceIndex, byte featureIndex, byte function, byte[] parameters)
        {
            return Request(writeHandle, readHandle, writeHid, readHid, deviceIndex, featureIndex, function, parameters, 700);
        }

        private static byte[] Request(SafeFileHandle writeHandle, SafeFileHandle readHandle, HidInfo writeHid, HidInfo readHid, byte deviceIndex, byte featureIndex, byte function, byte[] parameters, int timeout)
        {
            int writeLength = Math.Max(7, writeHid.OutputReportLength);
            byte[] request = new byte[writeLength];
            request[0] = writeLength >= 20 ? (byte)0x11 : (byte)0x10;
            request[1] = deviceIndex;
            request[2] = featureIndex;
            request[3] = (byte)((function << 4) | SoftwareId);
            for (int i = 0; i < parameters.Length && i < 3; i++) request[4 + i] = parameters[i];

            if (TraceEnabled) Console.WriteLine("TX " + BitConverter.ToString(request));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            int readLength = Math.Max(7, readHid.InputReportLength);
            byte[] response = new byte[readLength];
            int count;
            using (PendingRead pending = new PendingRead(readHandle, response))
            {
                if (!pending.Start()) return null;
                if (!WriteWithTimeout(writeHandle, request, 500)) return null;
                count = pending.Wait(timeout);
            }

            while (count > 0)
            {
                if (count >= 7)
                {
                    if (TraceEnabled) Console.WriteLine("RX " + BitConverter.ToString(response, 0, count));
                    if (response[1] == deviceIndex && (response[2] == 0x8F || response[2] == 0xFF)) return null;
                    if (response[1] == deviceIndex && response[2] == featureIndex && (response[3] & 0xF0) == (function << 4))
                    {
                        if (count == response.Length) return response;
                        byte[] exact = new byte[count];
                        Buffer.BlockCopy(response, 0, exact, 0, count);
                        return exact;
                    }
                }
                int remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                if (remaining <= 1) return null;
                response = new byte[readLength];
                count = ReadWithTimeout(readHandle, response, remaining);
            }
            return null;
        }

        private sealed class PendingRead : IDisposable
        {
            private readonly SafeFileHandle _handle;
            private readonly byte[] _buffer;
            private GCHandle _pinned;
            private IntPtr _eventHandle;
            private HidNative.OverlappedData _overlapped;
            private bool _started;
            private bool _completed;
            private uint _transferred;

            public PendingRead(SafeFileHandle handle, byte[] buffer)
            {
                _handle = handle;
                _buffer = buffer;
            }

            public bool Start()
            {
                _pinned = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
                _eventHandle = HidNative.CreateEvent(IntPtr.Zero, true, false, null);
                _overlapped = new HidNative.OverlappedData();
                _overlapped.EventHandle = _eventHandle;
                _started = true;
                _completed = HidNative.ReadFile(_handle, _pinned.AddrOfPinnedObject(), (uint)_buffer.Length, out _transferred, ref _overlapped);
                if (_completed) return true;
                return Marshal.GetLastWin32Error() == 997;
            }

            public int Wait(int timeout)
            {
                if (_completed) return (int)_transferred;
                if (HidNative.WaitForSingleObject(_eventHandle, (uint)Math.Max(1, timeout)) != 0)
                {
                    HidNative.CancelIoEx(_handle, IntPtr.Zero);
                    HidNative.WaitForSingleObject(_eventHandle, 100);
                    return -1;
                }
                return HidNative.GetOverlappedResult(_handle, ref _overlapped, out _transferred, false) ? (int)_transferred : -1;
            }

            public void Dispose()
            {
                if (_started && !_completed) HidNative.CancelIoEx(_handle, IntPtr.Zero);
                if (_eventHandle != IntPtr.Zero) HidNative.CloseHandle(_eventHandle);
                if (_pinned.IsAllocated) _pinned.Free();
            }
        }

        private static bool WriteWithTimeout(SafeFileHandle handle, byte[] buffer, int timeout)
        {
            uint transferred;
            return IoWithTimeout(handle, buffer, true, timeout, out transferred);
        }

        private static int ReadWithTimeout(SafeFileHandle handle, byte[] buffer, int timeout)
        {
            uint transferred;
            return IoWithTimeout(handle, buffer, false, timeout, out transferred) ? (int)transferred : -1;
        }

        private static bool IoWithTimeout(SafeFileHandle handle, byte[] buffer, bool write, int timeout, out uint transferred)
        {
            transferred = 0;
            GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            IntPtr eventHandle = HidNative.CreateEvent(IntPtr.Zero, true, false, null);
            try
            {
                HidNative.OverlappedData overlapped = new HidNative.OverlappedData();
                overlapped.EventHandle = eventHandle;
                bool completed = write
                    ? HidNative.WriteFile(handle, pinned.AddrOfPinnedObject(), (uint)buffer.Length, out transferred, ref overlapped)
                    : HidNative.ReadFile(handle, pinned.AddrOfPinnedObject(), (uint)buffer.Length, out transferred, ref overlapped);
                if (completed) return true;
                int error = Marshal.GetLastWin32Error();
                if (error != 997) return false;
                if (HidNative.WaitForSingleObject(eventHandle, (uint)Math.Max(1, timeout)) != 0)
                {
                    HidNative.CancelIoEx(handle, IntPtr.Zero);
                    HidNative.WaitForSingleObject(eventHandle, 100);
                    return false;
                }
                return HidNative.GetOverlappedResult(handle, ref overlapped, out transferred, false);
            }
            finally
            {
                if (eventHandle != IntPtr.Zero) HidNative.CloseHandle(eventHandle);
                pinned.Free();
            }
        }

        private static bool? DecodeCharging(byte status)
        {
            if (status == 0) return false;
            if (status == 1 || status == 2 || status == 4) return true;
            if (status == 3) return false;
            return null;
        }

        private static int? VoltageToPercent(int millivolts)
        {
            if (millivolts < 2800 || millivolts > 4500) return null;
            if (millivolts >= 4200) return 100;
            if (millivolts <= 3300) return 0;
            double normalized = (millivolts - 3300.0) / 900.0;
            return (int)Math.Round(Math.Pow(normalized, 0.72) * 100.0);
        }

        private static PeripheralKind GuessKind(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            if (lower.Contains("keyboard") || lower.Contains("keys") || lower.Contains("键盘")) return PeripheralKind.Keyboard;
            if (lower.Contains("head") || lower.Contains("speaker") || lower.Contains("耳机")) return PeripheralKind.Audio;
            return PeripheralKind.Mouse;
        }
    }
}
