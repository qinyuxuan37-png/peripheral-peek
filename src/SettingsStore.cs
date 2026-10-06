using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PeripheralPeek
{
    internal sealed class AppSettings
    {
        public string SelectedDeviceId { get; set; }
        public string SelectedPhysicalId { get; set; }
        public int SelectedVendorId { get; set; }
        public int SelectedProductId { get; set; }
        public string SelectedKind { get; set; }
        public bool HasManualSelection { get; set; }
        public bool StartWithWindows { get; set; }
        public int RefreshSeconds { get; set; }

        public AppSettings()
        {
            RefreshSeconds = 10;
        }
    }

    internal sealed class SettingsStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public SettingsStore()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeripheralPeek");
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "settings.json");
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_path)) return new AppSettings();
                string text = File.ReadAllText(_path, Encoding.UTF8);
                AppSettings value = _json.Deserialize<AppSettings>(text);
                if (value == null) return new AppSettings();
                if (value.RefreshSeconds < 3) value.RefreshSeconds = 10;
                return value;
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, _json.Serialize(settings), Encoding.UTF8);
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(temporary, _path);
        }
    }
}
