# Honor PC Helper — 在 Windows 托盘中控制荣耀 MagicBook 硬件

[English](README.md) | [Русский](README.ru.md)

[![最新版本](https://img.shields.io/github/v/release/Wintego/Honor-PC-Helper?label=%E4%B8%8B%E8%BD%BD)](https://github.com/Wintego/Honor-PC-Helper/releases/latest)
[![下载量](https://img.shields.io/github/downloads/Wintego/Honor-PC-Helper/total)](https://github.com/Wintego/Honor-PC-Helper/releases)
[![平台](https://img.shields.io/badge/%E5%B9%B3%E5%8F%B0-Windows%2010%20%7C%2011%20x64-blue)](#系统要求)

**Honor PC Helper** 是荣耀电脑管家（HONOR PC Manager）的轻量级开源替代方案：一款 Windows 托盘小工具，用于控制荣耀 MagicBook 笔记本的硬件功能——**电池充电阈值**、**键盘背光**、**性能模式**、**触控板振动与边缘手势**。它直接调用荣耀 BIOS 的 WMI 接口，与电脑管家所用的接口相同，因此无需安装整套厂商软件即可获得这些硬件设置。

单个绿色 `.exe` 文件：免安装、无后台服务、无遥测。

![荣耀 MagicBook 上的 Honor PC Helper 托盘菜单：电池充电阈值、键盘背光、性能模式与触控板设置](Assets/Screenshot-en.png)

## 功能

- **电池充电阈值** — 限制充电上限（例如 70-80%），长期插电时延长电池寿命
- **键盘背光** — 开启/关闭、熄灭超时、夜间自动开启计划；睡眠与新式待机（modern standby）唤醒后自动恢复
- **性能模式** — 在智能（均衡）模式与高性能模式之间切换，无需打开电脑管家
- **驱动管理** — 自动匹配任意受支持的荣耀笔记本型号，并从荣耀官方服务安全下载新版或缺失的驱动
- **应用更新** — 检查 GitHub Releases，并自动替换 Honor PC Helper 便携版程序，无需管理员权限提示
- **硬件监控** — 温度、风扇转速、充放电功率（瓦）直接显示在托盘提示中
- **触控板** — 振动反馈强度、边缘手势（左侧调亮度、右侧调音量）、沿左边缘滑动调节屏幕亮度并显示 Windows 原生 OSD
- **开机自启动** — 随 Windows 一起启动
- **界面语言** — 简体中文、英语、俄语，根据 Windows 显示语言自动选择

## 系统要求

- **Windows 10 或 11，x64**
- 提供荣耀 WMI BIOS 接口的**荣耀笔记本** — MagicBook 系列（MagicBook 14、15、16、X14、X16、Pro、Art）均具备
- 无需安装 .NET 运行时：发布版本为自包含程序
- **无需**安装或运行荣耀电脑管家

可用功能取决于机型与 BIOS 版本：触控板振动仅在配备压感触控板的机型上出现，部分机器可读取的传感器较少。不受支持的项目不会显示在托盘菜单中。

## 下载与使用

1. 从[最新发布版本](https://github.com/Wintego/Honor-PC-Helper/releases/latest)下载 `HonorPCHelper.exe`。
2. 直接运行。无需安装、无需依赖 — 图标会出现在系统托盘中。
3. 点击托盘图标即可打开菜单。

首次修改硬件设置时，Windows 会请求管理员权限以创建计划任务。之后程序运行不再弹出 UAC 提示。

如需检查驱动程序，请打开托盘菜单中的“驱动程序”。应用会根据荣耀官方目录匹配笔记本型号，并突出显示新版或缺失的软件包。点击突出显示的版本即可验证并下载安装程序；是否运行安装仍由你决定。

## 常见问题

### 不用电脑管家，如何限制荣耀 MagicBook 的电池充电？

打开托盘菜单，选择"电池"并设置充电区间。阈值通过荣耀 BIOS 的 WMI 接口写入，重启后依然有效，与电脑管家设置的效果相同。

### 如何开启键盘背光、让它不再自动熄灭？

在"键盘背光"子菜单中开启或关闭背光并设置熄灭超时；同一菜单里还有夜间自动开启的计划。

### 有没有轻量级的荣耀电脑管家替代方案？

本项目就是。Honor PC Helper 以单个绿色 exe 覆盖硬件设置和驱动更新，免安装、无后台服务。它不提供手机多屏协同。

### 可以卸载荣耀电脑管家只用这个工具吗？

如果你不需要 MagicRing，可以。电脑管家写入的数值（充电阈值、背光超时）保存在 BIOS 中并继续生效；Honor PC Helper 通过同一 WMI 接口读取这些设置，并从荣耀官方更新服务获取最新驱动。

### 需要管理员权限吗？

只需一次，用于创建执行硬件命令的计划任务。日常使用无需提权。

### 我的机型缺少部分菜单项

说明这些功能未被该机型的 BIOS 暴露。温度与风扇读数、触控板振动与边缘手势因机型和固件版本而异。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)：

```powershell
.\build.ps1
```

生成的文件位于 `dist\HonorPCHelper.exe`。

---

本项目与荣耀（HONOR）无关，未获其认可或支持。HONOR 与 MagicBook 为各自所有者的商标。
