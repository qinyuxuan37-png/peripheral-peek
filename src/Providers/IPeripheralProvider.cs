using System;
using System.Collections.Generic;
using PeripheralPeek.Native;

namespace PeripheralPeek.Providers
{
    internal interface IPeripheralProvider
    {
        string Name { get; }
        IList<PeripheralStatus> Scan();
    }

    internal interface ISharedScanProvider : IPeripheralProvider
    {
        IList<PeripheralStatus> Scan(DeviceScanSnapshot snapshot);
    }

    // Native device enumeration is expensive. A snapshot is scoped to one
    // refresh and initialized only if a provider actually needs each list.
    internal sealed class DeviceScanSnapshot
    {
        private IList<HidInfo> _hids;
        private IList<PnpDeviceInfo> _pnp;
        internal int HidEnumerations { get; private set; }
        internal int PnpEnumerations { get; private set; }

        public IList<HidInfo> Hids
        {
            get
            {
                if (_hids == null)
                {
                    _hids = HidNative.Enumerate();
                    HidEnumerations++;
                }
                return _hids;
            }
        }

        public IList<PnpDeviceInfo> Pnp
        {
            get
            {
                if (_pnp == null)
                {
                    _pnp = PnpNative.EnumeratePresent();
                    PnpEnumerations++;
                }
                return _pnp;
            }
        }
    }

    internal interface ICacheInvalidatable
    {
        void InvalidateCache();
    }

    internal static class ProviderLog
    {
        private const long MaxLogBytes = 256 * 1024;
        private static readonly object Gate = new object();
        private static string _lastMessage;
        private static DateTime _lastMessageAt;

        public static void Write(string source, Exception exception)
        {
            try
            {
                string directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeripheralPeek");
                System.IO.Directory.CreateDirectory(directory);
                string message = "[" + source + "] " + exception.GetType().Name + ": " + exception.Message;
                lock (Gate)
                {
                    DateTime now = DateTime.Now;
                    if (String.Equals(message, _lastMessage, StringComparison.Ordinal) && now - _lastMessageAt < TimeSpan.FromMinutes(1)) return;
                    _lastMessage = message;
                    _lastMessageAt = now;

                    string path = System.IO.Path.Combine(directory, "peripheralpeek.log");
                    if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length >= MaxLogBytes)
                    {
                        string previous = path + ".old";
                        if (System.IO.File.Exists(previous)) System.IO.File.Delete(previous);
                        System.IO.File.Move(path, previous);
                    }
                    string line = now.ToString("s") + " " + message + Environment.NewLine;
                    System.IO.File.AppendAllText(path, line);
                }
            }
            catch { }
        }
    }
}
