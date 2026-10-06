using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PeripheralPeek.Providers
{
    internal sealed class XInputProvider : IPeripheralProvider
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputBatteryInformation
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint userIndex, out XInputState state);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
        private static extern uint XInputGetBatteryInformation14(uint userIndex, byte deviceType, out XInputBatteryInformation batteryInformation);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState91(uint userIndex, out XInputState state);

        public string Name { get { return "Windows XInput"; } }

        public IList<PeripheralStatus> Scan()
        {
            List<PeripheralStatus> devices = new List<PeripheralStatus>();
            for (uint index = 0; index < 4; index++)
            {
                XInputState state;
                if (GetState(index, out state) != 0) continue;

                XInputBatteryInformation battery;
                bool hasBattery = GetBattery(index, out battery) == 0;
                // XInput's "wired" battery type also describes wireless USB
                // receivers. It does not prove that the controller has external
                // power, nor that its battery is readable through XInput.
                bool wirelessBattery = hasBattery && (battery.BatteryType == 2 || battery.BatteryType == 3);
                PeripheralStatus status = new PeripheralStatus
                {
                    Id = "xinput:" + index,
                    Name = devices.Count == 0 ? "Xbox 手柄" : "Xbox 手柄 " + (index + 1),
                    Kind = PeripheralKind.Gamepad,
                    Connection = wirelessBattery ? ConnectionKind.Wireless24 : ConnectionKind.Unknown,
                    LastUpdated = DateTime.Now,
                    Source = Name
                };

                if (wirelessBattery)
                {
                    switch (battery.BatteryLevel)
                    {
                        case 0: status.BatteryLevelText = "电量空"; break;
                        case 1: status.BatteryLevelText = "低电量"; break;
                        case 2: status.BatteryLevelText = "中等电量"; break;
                        case 3: status.BatteryLevelText = "高电量"; break;
                    }
                }
                devices.Add(status);
            }
            return devices;
        }

        private static uint GetState(uint index, out XInputState state)
        {
            try { return XInputGetState14(index, out state); }
            catch (DllNotFoundException) { return XInputGetState91(index, out state); }
            catch (EntryPointNotFoundException) { return XInputGetState91(index, out state); }
        }

        private static uint GetBattery(uint index, out XInputBatteryInformation battery)
        {
            try { return XInputGetBatteryInformation14(index, 0, out battery); }
            catch
            {
                battery = new XInputBatteryInformation();
                return 1;
            }
        }
    }
}
