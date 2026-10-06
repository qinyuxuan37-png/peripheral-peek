# Peripheral Peek

Peripheral Peek 是一个零配置的 Windows 外设状态栏。程序自动发现当前连接的外设，并在任务栏托盘区域左侧直接显示“设备名 + 电量/状态”（例如 `GPW 86%`）；单击文字可查看全部在线设备。

## 当前功能

- 自动发现当前连接的鼠标、键盘、XInput 兼容手柄和蓝牙外设。
- 不限定品牌或型号：按 Windows 物理设备容器把复合手柄的辅助鼠标/键盘接口合并，并根据 Xbox 兼容驱动节点去除重复的 XInput 条目。单手柄保留设备上报的真实型号；多手柄无法可靠对应 XInput 序号时不猜测电量归属。
- 通用识别带有 Receiver、Dongle、Wireless、2.4G 等接收器特征的 USB HID 外设，并标记为 `2.4G`，不限定厂商或型号。
- 按 Windows 设备容器识别当前连接的蓝牙耳机、音箱、鼠标、键盘和手柄，合并同一物理设备产生的多个系统节点。
- 识别具有明确无线接收器特征的 USB 音频设备，包括常见的 2.4G 耳机/音箱接收器。
- 原生 Windows 设备属性扫描读取可用的 `BatteryLife` 和 `BatteryPlusCharging`；设备不公开电量时显示“已连接”。
- 蓝牙设备还会读取 Windows 蓝牙协议栈公开的电量属性，匹配同一物理设备容器中的耳机服务节点；Windows 能显示的耳机电量现在也可出现在面板中。
- 对已确认连接、且系统属性未提供电量的蓝牙 LE 设备，额外尝试只读标准 GATT Battery Service（180F/2A19）；设备不提供该服务或读取失败时仍显示“已连接”。
- 所有设备约每 10 秒刷新一次；设备变化事件经过 500 毫秒防抖后立即补扫。
- 同一轮刷新复用 HID、PnP 设备快照；设备状态未变化时不重绘面板与任务栏文字，减少重复枚举和界面分配。
- 用设备容器 ID、接收器槽位和 HID 路径区分同型号设备，避免单纯按 VID/PID 合并。
- 运行日志会合并一分钟内的重复错误，并在 256 KB 时轮换，最多保留一份旧日志。
- 设备断开后自动从面板移除，不显示离线占位。
- 默认从在线设备中随机选择任务栏状态，直接显示设备简称与电量/状态，无需悬停或点击。
- 设备简称根据设备每次连接时上报的型号动态生成；已支持 GPW/GPW2/GPW3、Logitech G 系列型号和 MX Master/Anywhere 等常见命名，不保存某一只设备的固定别名。
- 单击面板中的任意设备，将其固定为任务栏主状态。
- 打开面板时使用约 210 毫秒的淡入与轻微上浮动画。
- 关闭面板时使用约 165 毫秒的淡出与轻微下移动画；再次点击任务栏文字即可关闭，点击面板外也会收起。
- 面板与任务栏固定保持 16 像素间距，保留完整圆角和阴影；打开时模糊采样背后的画面，再叠加轻柔渐变，任务栏文字在悬停和展开时显示圆角底板。
- Explorer 任务栏重建、显示器布局、DPI 或主题变化时主动重新寻找并定位任务栏文字；支持主任务栏不可用时选择有通知区域的次屏任务栏。
- 普通软件弹窗覆盖托盘时不再把任务栏文字误隐藏；只在任务栏本身不可见或 Windows 报告全屏／演示状态时隐藏。
- 外设面板采用贴近 Windows 11 任务栏的简洁样式，自动跟随任务栏明暗、附近底色与系统强调色。
- 手动选择的设备暂时断开时自动显示其他在线设备，重新连接后自动恢复。
- Logitech HID++ 电量读取，依次探测 Unified Battery、Battery Status 和 Battery Voltage。
- XInput 在确实公开无线电池等级时显示低/中/高；返回“有线”而实际可能是 2.4G 接收器时，不误报“外接供电”。
- 蓝牙连接状态通过 Windows 蓝牙接口核对，不把已配对但离线的设备显示为在线。
- 不包含低电量、断开或充满提醒。

## 使用

需要 64 位 Windows 和系统 .NET Framework。当前优先适配 Windows 11 底部任务栏；不保证所有 Windows 版本、设备协议或任务栏修改工具兼容。仓库只含源码，不含作者的设备设置、日志或编译产物。

首次下载请先在项目目录执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`（仅本次进程；先审阅脚本），然后运行：

```powershell
.\dist\PeripheralPeek.exe
```

单击任务栏中的状态文字打开面板；右键可以刷新、恢复随机选择、设置开机启动或退出。程序不再创建 `^` 隐藏区图标。

任务栏文字通过无焦点文字层贴合当前主任务栏，并定时在 Explorer 重启或任务栏尺寸变化后重新定位；任务栏被全屏应用遮住时文字也会隐藏。当前版本优先适配 Windows 11 底部任务栏。

设备协议诊断：

```powershell
.\dist\PeripheralPeek.Probe.exe --probe
.\dist\PeripheralPeek.Probe.exe --gatt-check
.\dist\PeripheralPeek.Probe.exe --test-taskbar-reposition
.\dist\PeripheralPeek.Probe.exe --test-taskbar-recover
.\dist\PeripheralPeek.Probe.exe --test-overlay-visibility
```

## 构建

本项目无需安装 Visual Studio 或 .NET SDK，使用 Windows 自带的 .NET Framework 编译器：

```powershell
.\build.ps1
```

输出位于 `dist`。

## 技术边界

设备接入和移除可以统一自动发现，但 2.4G 接收器在 Windows 中本质上是 USB 设备，没有统一的“这是无线接收器”标志，也没有统一电量接口。程序会根据 Receiver、Dongle、Wireless、2.4G、无线、接收器等通用特征判断；无法确定时保守显示为 USB。未知设备仍会自动出现，只有设备通过标准属性、Windows 驱动或已支持的厂商协议公开电量时，才能显示电量。

当前首个厂商协议实现为 Logitech HID++。后续品牌应以新的 `IPeripheralProvider` 接入，不需要用户手动登记设备型号。

某些 2.4G 手柄在 Windows 中以 USB/XInput 设备出现，XInput 返回“有线”且系统 PnP 节点无电量属性。此时程序只能显示“已连接”；要显示准确百分比，需要厂商公开或逆向确认接收器的电量协议，不能仅从 VID/PID 或电池容量推算。

蓝牙经典设备可由系统蓝牙接口确认连接状态；只有 LE 连接且系统未在设备节点公开连接状态的设备，当前版本会保守隐藏。蓝牙耳机电量仍取决于 Windows 是否公开可读取的设备属性；其中一条蓝牙电量属性未列入公开 SDK，未来 Windows 版本可能调整。

## 开发与隐私

`src/Providers/` 为设备提供器，`src/Native/` 为 Windows 接口，`src/UI/` 为界面。无需第三方 NuGet 包。构建后可运行 `dist/PeripheralPeek.Probe.exe --test-device-names` 检查名称显示；该命令只输出样例，不验证真实硬件兼容性。其余硬件和任务栏诊断需要本机实际设备与桌面会话。

用户设置保存在 `%LOCALAPPDATA%\PeripheralPeek\settings.json`。诊断输出可能包含设备路径和标识，分享前请脱敏。编译输出和日志已加入 Git 忽略规则。

## 许可证

原创代码采用 [MIT](LICENSE)。本项目与设备品牌没有隶属关系，不附带厂商软件或固件。
