using System;
using System.Text.RegularExpressions;

namespace PeripheralPeek
{
    internal enum PeripheralKind
    {
        Mouse,
        Keyboard,
        Gamepad,
        Audio,
        Other
    }

    internal enum ConnectionKind
    {
        Usb,
        Bluetooth,
        Wireless24,
        Unknown
    }

    internal sealed class PeripheralStatus
    {
        public string Id { get; set; }
        public string PhysicalId { get; set; }
        public string Name { get; set; }
        public PeripheralKind Kind { get; set; }
        public ConnectionKind Connection { get; set; }
        public int? BatteryPercent { get; set; }
        public string BatteryLevelText { get; set; }
        public bool? IsCharging { get; set; }
        public bool IsExternallyPowered { get; set; }
        public bool IsXInputCompatible { get; set; }
        public bool IsSleeping { get; set; }
        public DateTime LastUpdated { get; set; }
        public string Source { get; set; }
        public int VendorId { get; set; }
        public int ProductId { get; set; }

        public bool HasBattery
        {
            get { return BatteryPercent.HasValue || !String.IsNullOrEmpty(BatteryLevelText); }
        }

        public string ConnectionText
        {
            get
            {
                switch (Connection)
                {
                    case ConnectionKind.Usb: return "USB";
                    case ConnectionKind.Bluetooth: return "蓝牙";
                    case ConnectionKind.Wireless24: return "2.4G";
                    default: return "已连接";
                }
            }
        }

        public string ValueText
        {
            get
            {
                if (BatteryPercent.HasValue) return BatteryPercent.Value + "%";
                if (!String.IsNullOrEmpty(BatteryLevelText)) return BatteryLevelText;
                if (IsExternallyPowered) return "外接供电";
                return "已连接";
            }
        }

        public string TrayText
        {
            get
            {
                if (BatteryPercent.HasValue)
                {
                    if (BatteryPercent.Value >= 100) return "满";
                    return BatteryPercent.Value.ToString();
                }
                if (!String.IsNullOrEmpty(BatteryLevelText))
                {
                    if (BatteryLevelText.IndexOf("低", StringComparison.Ordinal) >= 0) return "低";
                    if (BatteryLevelText.IndexOf("中", StringComparison.Ordinal) >= 0) return "中";
                    if (BatteryLevelText.IndexOf("高", StringComparison.Ordinal) >= 0 || BatteryLevelText.IndexOf("满", StringComparison.Ordinal) >= 0) return "高";
                }
                if (IsExternallyPowered) return "电";
                return KindGlyph;
            }
        }

        public string TaskbarDisplayText
        {
            get { return ShortDisplayName + " " + ValueText; }
        }

        public string ShortDisplayName
        {
            get
            {
                string value = Name ?? String.Empty;
                string upper = value.ToUpperInvariant();
                if (Kind == PeripheralKind.Gamepad && upper.IndexOf("XBOX", StringComparison.Ordinal) >= 0) return "Xbox";
                if (upper.IndexOf("MADE68", StringComparison.Ordinal) >= 0) return "MADE68";
                if (Kind == PeripheralKind.Mouse)
                {
                    if (Regex.IsMatch(upper, @"\bPRO\s*X\s*3\b.*\bSUPERSTRIKE\b") || upper.IndexOf("SUPERLIGHT 3", StringComparison.Ordinal) >= 0) return "GPW3";
                    if (upper.IndexOf("SUPERLIGHT 2", StringComparison.Ordinal) >= 0) return "GPW2";
                    if (upper.IndexOf("SUPERLIGHT", StringComparison.Ordinal) >= 0) return "GPW";
                    if (Regex.IsMatch(upper, @"\bPRO\s*X\s*2\b.*\bSUPERSTRIKE\b")) return "PRO X2";

                    Match gModel = Regex.Match(upper, @"\bG\d{3}[A-Z]*\b");
                    if (gModel.Success) return gModel.Value;
                    Match mxModel = Regex.Match(upper, @"\bMX\s+(MASTER|ANYWHERE)\s+[0-9A-Z]+\b");
                    if (mxModel.Success) return ToTitleModel(mxModel.Value);
                }
                if (upper.IndexOf("USB RECEIVER", StringComparison.Ordinal) >= 0)
                {
                    switch (Kind)
                    {
                        case PeripheralKind.Mouse: return "鼠标";
                        case PeripheralKind.Keyboard: return "键盘";
                    }
                }
                if (String.IsNullOrWhiteSpace(value)) return KindGlyph;
                value = value.Trim();
                return value.Length <= 13 ? value : value.Substring(0, 12) + "…";
            }
        }

        private static string ToTitleModel(string value)
        {
            string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < parts.Length - 1; i++)
                parts[i] = Char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
            return String.Join(" ", parts);
        }

        public string KindGlyph
        {
            get
            {
                switch (Kind)
                {
                    case PeripheralKind.Mouse: return "鼠";
                    case PeripheralKind.Keyboard: return "键";
                    case PeripheralKind.Gamepad: return "柄";
                    case PeripheralKind.Audio: return "音";
                    default: return "设";
                }
            }
        }

        public PeripheralStatus Clone()
        {
            return (PeripheralStatus)MemberwiseClone();
        }
    }
}
