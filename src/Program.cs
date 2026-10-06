using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PeripheralPeek.Providers;
using PeripheralPeek.UI;
using PeripheralPeek.Native;

namespace PeripheralPeek
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Any(a => String.Equals(a, "--render-preview", StringComparison.OrdinalIgnoreCase))) return RenderPreview();
            if (args.Any(a => String.Equals(a, "--capture-taskbar", StringComparison.OrdinalIgnoreCase))) return CaptureTaskbar();
            if (args.Any(a => String.Equals(a, "--test-taskbar-click", StringComparison.OrdinalIgnoreCase))) return TestTaskbarClick();
            if (args.Any(a => String.Equals(a, "--test-popup-toggle", StringComparison.OrdinalIgnoreCase))) return TestPopupToggle();
            if (args.Any(a => String.Equals(a, "--test-device-names", StringComparison.OrdinalIgnoreCase))) return TestDeviceNames();
            if (args.Any(a => String.Equals(a, "--test-device-change", StringComparison.OrdinalIgnoreCase))) return TestDeviceChange();
            if (args.Any(a => String.Equals(a, "--test-taskbar-reposition", StringComparison.OrdinalIgnoreCase))) return TestTaskbarReposition();
            if (args.Any(a => String.Equals(a, "--test-taskbar-recover", StringComparison.OrdinalIgnoreCase))) return TestTaskbarRecover();
            if (args.Any(a => String.Equals(a, "--test-overlay-visibility", StringComparison.OrdinalIgnoreCase))) return TestOverlayVisibility();
            if (args.Any(a => String.Equals(a, "--hid-dump", StringComparison.OrdinalIgnoreCase))) return RunHidDump();
            if (args.Any(a => String.Equals(a, "--hidpp-trace", StringComparison.OrdinalIgnoreCase))) return RunHidppTrace();
            if (args.Any(a => String.Equals(a, "--probe", StringComparison.OrdinalIgnoreCase))) return RunProbe();
            if (args.Any(a => String.Equals(a, "--pnp-dump", StringComparison.OrdinalIgnoreCase))) return RunPnpDump();
            if (args.Any(a => String.Equals(a, "--wireless-bench", StringComparison.OrdinalIgnoreCase))) return RunWirelessBench();
            if (args.Any(a => String.Equals(a, "--bluetooth-summary", StringComparison.OrdinalIgnoreCase))) return RunBluetoothSummary();
            if (args.Any(a => String.Equals(a, "--identity-check", StringComparison.OrdinalIgnoreCase))) return RunIdentityCheck();
            if (args.Any(a => String.Equals(a, "--composite-check", StringComparison.OrdinalIgnoreCase))) return RunCompositeCheck();
            if (args.Any(a => String.Equals(a, "--gatt-check", StringComparison.OrdinalIgnoreCase))) return RunGattCheck();

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\PeripheralPeek.SingleInstance", out created))
            {
                if (!created) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                using (TrayApplicationContext context = new TrayApplicationContext()) Application.Run(context);
            }
            return 0;
        }

        private static int RenderPreview()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            List<PeripheralStatus> sample = new List<PeripheralStatus>
            {
                new PeripheralStatus { Id = "mouse", Name = "PRO X SUPERLIGHT", Kind = PeripheralKind.Mouse, Connection = ConnectionKind.Wireless24, BatteryPercent = 86, LastUpdated = DateTime.Now },
                new PeripheralStatus { Id = "keyboard", Name = "MADE68PRO+ ZG", Kind = PeripheralKind.Keyboard, Connection = ConnectionKind.Usb, LastUpdated = DateTime.Now },
                new PeripheralStatus { Id = "gamepad", Name = "Xbox 手柄", Kind = PeripheralKind.Gamepad, Connection = ConnectionKind.Wireless24, BatteryLevelText = "中等电量", LastUpdated = DateTime.Now },
                new PeripheralStatus { Id = "audio", Name = "蓝牙音响", Kind = PeripheralKind.Audio, Connection = ConnectionKind.Bluetooth, BatteryPercent = 58, LastUpdated = DateTime.Now }
            };
            using (PopupForm form = new PopupForm())
            {
                form.UpdateDevices(sample, "mouse");
                form.Show();
                Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PeripheralPeek.preview.png");
                    bitmap.Save(path, ImageFormat.Png);
                    Console.WriteLine(path);
                }
                using (TaskbarStatusForm status = new TaskbarStatusForm(false))
                {
                    status.UpdateStatus(sample[0]);
                    using (Bitmap statusBitmap = new Bitmap(status.Width, status.Height))
                    {
                        status.DrawToBitmap(statusBitmap, new Rectangle(0, 0, statusBitmap.Width, statusBitmap.Height));
                        string statusPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PeripheralPeek.taskbar-preview.png");
                        statusBitmap.Save(statusPath, ImageFormat.Png);
                        Console.WriteLine(statusPath);
                    }
                }
                form.Hide();
            }
            return 0;
        }

        private static int CaptureTaskbar()
        {
            Rectangle taskbar = TaskbarStatusForm.GetTaskbarScreenBounds();
            Rectangle widget = TaskbarStatusForm.GetLiveWidgetScreenBounds();
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("Taskbar: " + taskbar);
            Console.WriteLine("PeripheralPeek: " + widget);
            if (taskbar.IsEmpty || widget.IsEmpty)
            {
                Console.WriteLine("未找到已嵌入的任务栏文字窗口。");
                return 2;
            }
            using (Bitmap bitmap = new Bitmap(taskbar.Width, taskbar.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(taskbar.Location, Point.Empty, taskbar.Size);
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PeripheralPeek.taskbar-live.png");
                bitmap.Save(path, ImageFormat.Png);
                Console.WriteLine(path);
            }
            return 0;
        }

        private static int TestTaskbarClick()
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (!TaskbarStatusForm.SendTestPrimaryClick())
            {
                Console.WriteLine("未找到任务栏文字窗口。");
                return 2;
            }
            Thread.Sleep(600);
            Rectangle popup = TaskbarStatusForm.GetTopLevelWindowBounds("PeripheralPeek.DevicePopup");
            Console.WriteLine("Popup: " + popup);
            if (popup.IsEmpty)
            {
                Console.WriteLine("点击消息已发送，但面板没有显示。");
                return 3;
            }
            using (Bitmap bitmap = new Bitmap(popup.Width, popup.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(popup.Location, Point.Empty, popup.Size);
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PeripheralPeek.popup-live.png");
                bitmap.Save(path, ImageFormat.Png);
                Console.WriteLine(path);
            }
            return 0;
        }

        private static int TestPopupToggle()
        {
            const string title = "PeripheralPeek.DevicePopup";
            Console.OutputEncoding = Encoding.UTF8;
            if (TaskbarStatusForm.IsTopLevelWindowVisible(title))
            {
                TaskbarStatusForm.SendTestPrimaryClick();
                Thread.Sleep(250);
            }

            if (!TaskbarStatusForm.SendTestPrimaryClick()) return 2;
            Thread.Sleep(320);
            bool opened = TaskbarStatusForm.IsTopLevelWindowVisible(title);
            if (!opened) { Console.WriteLine("首次点击未打开面板。"); return 3; }

            TaskbarStatusForm.SendTestPrimaryClick();
            Thread.Sleep(55);
            bool visibleDuringClose = TaskbarStatusForm.IsTopLevelWindowVisible(title);
            Thread.Sleep(200);
            bool closed = !TaskbarStatusForm.IsTopLevelWindowVisible(title);
            if (!visibleDuringClose || !closed)
            {
                Console.WriteLine("第二次点击的关闭动画或最终隐藏状态异常。");
                return 4;
            }

            TaskbarStatusForm.SendTestPrimaryClick();
            Thread.Sleep(320);
            bool reopened = TaskbarStatusForm.IsTopLevelWindowVisible(title);
            TaskbarStatusForm.SendTestPrimaryClick();
            Thread.Sleep(220);
            TaskbarStatusForm.SendTestPrimaryClick();
            Thread.Sleep(45);
            TaskbarStatusForm.SendTestPrimaryClick();
            Thread.Sleep(230);
            bool rapidClose = !TaskbarStatusForm.IsTopLevelWindowVisible(title);
            Console.WriteLine("打开=" + opened + "，关闭过渡=" + visibleDuringClose + "，关闭=" + closed + "，再次打开=" + reopened + "，快速连点关闭=" + rapidClose);
            return reopened && rapidClose ? 0 : 5;
        }

        private static int TestDeviceNames()
        {
            Console.OutputEncoding = Encoding.UTF8;
            string[] names =
            {
                "PRO X SUPERLIGHT 2 SE",
                "PRO X3 SUPERSTRIKE",
                "PRO X2 SUPERSTRIKE",
                "Logitech G502 X PLUS LIGHTSPEED",
                "Logitech MX MASTER 3S"
            };
            foreach (string name in names)
            {
                PeripheralStatus status = new PeripheralStatus
                {
                    Name = name,
                    Kind = PeripheralKind.Mouse,
                    VendorId = 0x046D,
                    BatteryPercent = 88
                };
                Console.WriteLine(name + " => " + status.TaskbarDisplayText);
            }
            return 0;
        }

        private static int TestDeviceChange()
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool sent = TaskbarStatusForm.SendTestDeviceChange();
            Console.WriteLine(sent ? "已发送设备变化测试消息。" : "未找到任务栏文字窗口。");
            return sent ? 0 : 2;
        }

        private static int TestTaskbarReposition()
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool sent = TaskbarStatusForm.SendTestDisplayChange();
            Thread.Sleep(300);
            Rectangle taskbar = TaskbarStatusForm.GetTaskbarScreenBounds();
            Rectangle widget = TaskbarStatusForm.GetLiveWidgetScreenBounds();
            bool inside = sent && !taskbar.IsEmpty && !widget.IsEmpty && taskbar.IntersectsWith(widget);
            Console.WriteLine(inside ? "显示设置变化后任务栏文字位置正常。" : "任务栏文字重新定位检查失败。");
            return inside ? 0 : 2;
        }

        private static int TestTaskbarRecover()
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool sent = TaskbarStatusForm.SendTestTaskbarCreated();
            Thread.Sleep(300);
            Rectangle taskbar = TaskbarStatusForm.GetTaskbarScreenBounds();
            Rectangle widget = TaskbarStatusForm.GetLiveWidgetScreenBounds();
            bool inside = sent && !taskbar.IsEmpty && !widget.IsEmpty && taskbar.IntersectsWith(widget);
            Console.WriteLine(inside ? "模拟 Explorer 任务栏重建后文字已重新附着。" : "任务栏重建恢复检查失败。");
            return inside ? 0 : 2;
        }

        private static int TestOverlayVisibility()
        {
            Application.EnableVisualStyles();
            Console.OutputEncoding = Encoding.UTF8;
            Rectangle taskbar = TaskbarStatusForm.GetTaskbarScreenBounds();
            if (taskbar.IsEmpty || !TaskbarStatusForm.IsTopLevelWindowVisible("PeripheralPeek.TaskbarStatus")) return 2;
            using (Form blocker = new Form())
            {
                blocker.FormBorderStyle = FormBorderStyle.None;
                blocker.ShowInTaskbar = false;
                blocker.StartPosition = FormStartPosition.Manual;
                blocker.TopMost = true;
                blocker.Bounds = new Rectangle(taskbar.Right - 80, taskbar.Top + 2, 78, Math.Max(10, taskbar.Height - 4));
                blocker.Show();
                Application.DoEvents();
                Thread.Sleep(1700);
                Application.DoEvents();
                bool stayedVisible = TaskbarStatusForm.IsTopLevelWindowVisible("PeripheralPeek.TaskbarStatus");
                blocker.Hide();
                Thread.Sleep(1700);
                bool restored = TaskbarStatusForm.IsTopLevelWindowVisible("PeripheralPeek.TaskbarStatus");
                Console.WriteLine("弹窗覆盖托盘时仍显示=" + stayedVisible + "，弹窗关闭后显示=" + restored);
                return stayedVisible && restored ? 0 : 3;
            }
        }

        private static int RunHidppTrace()
        {
            Console.OutputEncoding = Encoding.UTF8;
            LogitechHidppProvider.TraceEnabled = true;
            IList<PeripheralStatus> devices = new LogitechHidppProvider().Scan();
            Console.WriteLine("HID++ devices: " + devices.Count);
            foreach (PeripheralStatus device in devices) Console.WriteLine(device.Name + " " + device.ValueText);
            return 0;
        }

        private static int RunHidDump()
        {
            Console.OutputEncoding = Encoding.UTF8;
            foreach (HidInfo hid in HidNative.Enumerate())
            {
                Console.WriteLine("VID={0:x4} PID={1:x4} UP={2:x4} U={3:x4} IN={4} OUT={5} PRODUCT={6} PATH={7}",
                    hid.VendorId, hid.ProductId, hid.UsagePage, hid.Usage, hid.InputReportLength, hid.OutputReportLength, hid.Product, hid.Path);
            }
            return 0;
        }

        private static int RunProbe()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("Peripheral Peek 设备探测");
            Console.WriteLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine();

            List<IPeripheralProvider> providers = new List<IPeripheralProvider>
            {
                new GenericHidProvider(),
                new XInputProvider(),
                new BluetoothProvider(),
                new LogitechHidppProvider()
            };
            int total = 0;
            List<PeripheralStatus> found = new List<PeripheralStatus>();
            DeviceScanSnapshot snapshot = new DeviceScanSnapshot();
            foreach (IPeripheralProvider provider in providers)
            {
                Console.WriteLine("[" + provider.Name + "]");
                try
                {
                    ISharedScanProvider shared = provider as ISharedScanProvider;
                    IList<PeripheralStatus> devices = shared == null ? provider.Scan() : shared.Scan(snapshot);
                    found.AddRange(devices);
                    total += devices.Count;
                    if (devices.Count == 0) Console.WriteLine("  未发现设备或设备未提供可读取状态");
                    foreach (PeripheralStatus device in devices)
                    {
                        Console.WriteLine("  {0} | {1} | {2} | {3} | {4}", device.Name, device.Kind, device.ConnectionText, device.ValueText, device.Id);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  错误: " + ex.Message);
                }
                Console.WriteLine();
            }
            Console.WriteLine("探测结果条目: " + total);
            Console.WriteLine("本轮系统枚举: HID " + snapshot.HidEnumerations + " 次，PnP " + snapshot.PnpEnumerations + " 次");
            using (DeviceMonitor monitor = new DeviceMonitor())
            {
                Console.WriteLine("面板最终显示:");
                foreach (PeripheralStatus device in monitor.Merge(found))
                    Console.WriteLine("  {0} | {1} | {2} | {3}", device.Name, device.Kind, device.ConnectionText, device.ValueText);
            }
            return 0;
        }

        private static int RunPnpDump()
        {
            Console.OutputEncoding = Encoding.UTF8;
            foreach (IGrouping<Guid, PnpDeviceInfo> group in PnpNative.EnumeratePresent().GroupBy(d => d.ContainerId))
            {
                IList<PnpDeviceInfo> devices = group.ToList();
                if (!devices.Any(d => d.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))) continue;
                Console.WriteLine("CONTAINER " + group.Key);
                foreach (PnpDeviceInfo device in devices)
                    Console.WriteLine("  {0} | {1} | {2} | connected={3} | battery={4}", device.InstanceId, device.ClassName, device.FriendlyName, device.IsConnected, device.BatteryPercent);
            }
            return 0;
        }

        private static int RunWirelessBench()
        {
            Console.OutputEncoding = Encoding.UTF8;
            using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                IList<PeripheralStatus> devices = new BluetoothProvider().Scan();
                watch.Stop();
                process.Refresh();
                Console.WriteLine("原生无线扫描: " + watch.ElapsedMilliseconds + " ms");
                Console.WriteLine("已连接无线设备: " + devices.Count);
                Console.WriteLine("探测进程工作集: " + Math.Round(process.WorkingSet64 / 1048576.0, 1) + " MB");
            }
            return 0;
        }

        private static int RunBluetoothSummary()
        {
            IDictionary<string, bool> states = BluetoothNative.GetKnownDeviceStates();
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("系统记录的蓝牙设备: " + states.Count);
            Console.WriteLine("其中当前连接: " + states.Values.Count(value => value));
            return 0;
        }

        private static int RunIdentityCheck()
        {
            List<PeripheralStatus> input = new List<PeripheralStatus>
            {
                new PeripheralStatus { Id = "hid:a", PhysicalId = "container:a", Name = "USB Receiver", Kind = PeripheralKind.Mouse, VendorId = 0x046D, ProductId = 0xC547, Source = "通用 HID" },
                new PeripheralStatus { Id = "hid:b", PhysicalId = "container:b", Name = "USB Receiver", Kind = PeripheralKind.Mouse, VendorId = 0x046D, ProductId = 0xC547, Source = "通用 HID" },
                new PeripheralStatus { Id = "logitech:a:1", PhysicalId = "container:a:slot:1", Name = "PRO X SUPERLIGHT 2", Kind = PeripheralKind.Mouse, VendorId = 0x046D, ProductId = 0xC547, BatteryPercent = 38, Source = "Logitech HID++" }
            };
            using (DeviceMonitor monitor = new DeviceMonitor())
            {
                IList<PeripheralStatus> merged = monitor.Merge(input);
                bool valid = merged.Count == 2 && merged.Any(d => d.Id == "hid:b") && merged.Any(d => d.Id == "logitech:a:1" && d.BatteryPercent == 38);
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine(valid ? "同型号双设备与接收器合并检查通过。" : "同型号双设备检查失败。");
                return valid ? 0 : 2;
            }
        }

        private static int RunCompositeCheck()
        {
            Console.OutputEncoding = Encoding.UTF8;
            List<PeripheralStatus> single = new List<PeripheralStatus>
            {
                new PeripheralStatus { Id = "hid:p5:mouse", PhysicalId = "container:p5", Name = "P5 8K", Kind = PeripheralKind.Mouse, VendorId = 0x36E6, ProductId = 0x3013, Source = "通用 HID" },
                new PeripheralStatus { Id = "hid:p5:keyboard", PhysicalId = "container:p5", Name = "P5 8K", Kind = PeripheralKind.Keyboard, VendorId = 0x36E6, ProductId = 0x3013, Source = "通用 HID" },
                new PeripheralStatus { Id = "hid:p5:gamepad", PhysicalId = "container:p5", Name = "Controller (P5 8K)", Kind = PeripheralKind.Gamepad, VendorId = 0x36E6, ProductId = 0x3013, Source = "通用 HID", IsXInputCompatible = true },
                new PeripheralStatus { Id = "xinput:0", Name = "Xbox 手柄", Kind = PeripheralKind.Gamepad, Source = "Windows XInput", Connection = ConnectionKind.Unknown },
                new PeripheralStatus { Id = "hid:other:mouse", PhysicalId = "container:other", Name = "Another Mouse", Kind = PeripheralKind.Mouse, Source = "通用 HID" }
            };
            GenericHidProvider.RemoveCompositeDuplicatesForTest(single);
            List<PeripheralStatus> multiple = new List<PeripheralStatus>
            {
                new PeripheralStatus { Id = "hid:a:mouse", PhysicalId = "container:a", Name = "Brand A", Kind = PeripheralKind.Mouse, VendorId = 0x1111, ProductId = 0x0001, Source = "通用 HID" },
                new PeripheralStatus { Id = "hid:a:pad", PhysicalId = "container:a", Name = "Brand A", Kind = PeripheralKind.Gamepad, VendorId = 0x1111, ProductId = 0x0001, Source = "通用 HID", IsXInputCompatible = true },
                new PeripheralStatus { Id = "hid:b:keyboard", PhysicalId = "container:b", Name = "Brand B", Kind = PeripheralKind.Keyboard, VendorId = 0x2222, ProductId = 0x0002, Source = "通用 HID" },
                new PeripheralStatus { Id = "hid:b:pad", PhysicalId = "container:b", Name = "Brand B", Kind = PeripheralKind.Gamepad, VendorId = 0x2222, ProductId = 0x0002, Source = "通用 HID", IsXInputCompatible = true },
                new PeripheralStatus { Id = "xinput:0", Name = "Xbox 手柄", Kind = PeripheralKind.Gamepad, Source = "Windows XInput" },
                new PeripheralStatus { Id = "xinput:1", Name = "Xbox 手柄 2", Kind = PeripheralKind.Gamepad, Source = "Windows XInput" },
                new PeripheralStatus { Id = "hid:other:keyboard", PhysicalId = "container:other", Name = "Real Keyboard", Kind = PeripheralKind.Keyboard, Source = "通用 HID" }
            };
            GenericHidProvider.RemoveCompositeDuplicatesForTest(multiple);
            List<PeripheralStatus> unrelated = new List<PeripheralStatus>
            {
                new PeripheralStatus { Id = "hid:directinput", PhysicalId = "container:directinput", Name = "DirectInput Pad", Kind = PeripheralKind.Gamepad, Source = "通用 HID" },
                new PeripheralStatus { Id = "xinput:0", Name = "Xbox 手柄", Kind = PeripheralKind.Gamepad, Source = "Windows XInput" }
            };
            using (DeviceMonitor monitor = new DeviceMonitor())
            {
                IList<PeripheralStatus> one = monitor.Merge(single);
                IList<PeripheralStatus> two = monitor.Merge(multiple);
                IList<PeripheralStatus> separate = monitor.Merge(unrelated);
                bool valid = one.Count == 2 && one.Any(d => d.Id == "hid:p5:gamepad" && d.Name == "Controller (P5 8K)" && d.ValueText == "已连接") &&
                    one.Any(d => d.Id == "hid:other:mouse") &&
                    two.Count == 3 && two.Any(d => d.Id == "hid:a:pad") && two.Any(d => d.Id == "hid:b:pad") &&
                    two.Any(d => d.Id == "hid:other:keyboard") &&
                    separate.Count == 2;
                Console.WriteLine(valid ? "单只／两只不同品牌复合手柄及独立手柄检查通过。" : "复合手柄节点合并检查失败。");
                return valid ? 0 : 2;
            }
        }

        private static int RunGattCheck()
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool layout = BluetoothGattNative.UuidSize == 20 && BluetoothGattNative.CharacteristicSize == 36;
            IList<PnpDeviceInfo> nodes = PnpNative.EnumeratePresent();
            List<Guid> connected = nodes.Where(d => d.InstanceId.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase) && d.IsConnected == true)
                .Select(d => d.ContainerId).Distinct().ToList();
            // With no live LE device, still exercise native service enumeration.
            IDictionary<Guid, int> values = BluetoothGattNative.ReadBatteryLevels(
                connected.Count == 0 ? new List<Guid> { Guid.NewGuid() } : connected);
            Console.WriteLine("GATT 结构布局: " + (layout ? "正确" : "错误"));
            Console.WriteLine("已连接 LE 设备: " + connected.Count + "，读到标准电量: " + values.Count);
            return layout ? 0 : 2;
        }
    }
}
