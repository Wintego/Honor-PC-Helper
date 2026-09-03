# Honor PC Helper — 在 Windows 托盘中控制荣耀 MagicBook 硬件

[English](README.md) | [Русский](README.ru.md)

[![最新版本](https://img.shields.io/github/v/release/Wintego/Honor-PC-Helper?label=%E4%B8%8B%E8%BD%BD)](https://github.com/Wintego/Honor-PC-Helper/releases/latest)
[![下载量](https://img.shields.io/github/downloads/Wintego/Honor-PC-Helper/total)](https://github.com/Wintego/Honor-PC-Helper/releases)
[![平台](https://img.shields.io/badge/%E5%B9%B3%E5%8F%B0-Windows%2010%20%7C%2011%20x64-blue)](#系统要求)

**Honor PC Helper** 是荣耀电脑管家（HONOR PC Manager）在硬件设置方面的开源替代方案：电池充电阈值、键盘背光、性能模式、触控板振动与边缘手势，以及从荣耀官方目录获取驱动更新。它直接调用荣耀 BIOS 的 WMI 接口——与电脑管家相同的 `OemWMIMethod` 调用——因此厂商整套软件既不必安装，也不必运行。

单个绿色 `.exe` 文件（约 49 MB，自包含）：免安装、无服务、无后台定时扫描、无遥测；在你打开**驱动程序**之前不产生任何网络请求。

![荣耀 MagicBook 上的 Honor PC Helper 托盘菜单：充电限制、键盘背光、性能模式与触控板设置](Assets/Screenshot-en.png)

## 托盘菜单各项说明

| 菜单项 | 可选值 | 作用 |
| --- | --- | --- |
| **充电限制** | 关闭（0–100%）、居家 40–70%、办公 70–90%、出行 95–100% | 将两个阈值写入电源控制器。重启后仍然有效，程序未运行时同样生效 |
| **键盘 → 背光** | 关闭、弱、强 | 设置背光亮度。用键盘按键所做的更改会被识别并反映在菜单中 |
| **键盘 → 超时** | 不关闭、15 秒、30 秒、1 分钟（默认）、5 分钟 | 空闲多久后由固件熄灭背光 |
| **键盘 → 计划** | 开关、开启时间、关闭时间、亮度 | 在整点自动开启和关闭背光。手动更改亮度会让计划暂停到下一个时间点 |
| **触控板 → 振动强度** | 低、中、高 | 压感触控板的振动反馈强度。仅在配备该触控板的机型上显示 |
| **触控板 → 边缘手势** | 亮度（左边缘）、音量（右边缘） | 启用或禁用单指沿边缘的垂直滑动 |
| **高性能模式** | 复选框 | 与 **Fn+P** 等效。需要连接电源且电量不低于 20%；进入睡眠或拔掉电源时自动关闭。开启期间托盘图标为实心 |
| **驱动程序** | — | 打开驱动与 BIOS 窗口，见[驱动程序](#驱动程序) |
| **开机自启动** | 复选框 | 添加或移除 `HKCU\…\Run` 项 |

将鼠标悬停在托盘图标上会显示实时状态：模式、背光亮度、充电区间、充放电功率（瓦）、CPU 与电池温度、两个风扇的转速。传感器最多每 5 秒读取一次，且仅在指针位于图标上时读取。

背光亮度、振动强度和边缘手势会在唤醒后（包括新式待机，此时使用点亮屏幕的事件而非不可靠的唤醒事件）以及触控板重新连接后重新应用——固件会遗忘这三项设置。

界面语言跟随 Windows 显示语言：简体中文、英语、俄语。

## 系统要求

- Windows 10 或 11，x64
- 提供荣耀 BIOS WMI 接口（`root\WMI` 中的 `OemWMIMethod`）的荣耀笔记本 —— MagicBook 系列均具备
- 无需安装 .NET 运行时：发布版本为自包含程序
- **无需**安装或运行荣耀电脑管家

在 HONOR MagicBook Pro 14 2026（`ZQC-P`，BIOS 1.10，Windows 11 26200）上开发并验证。其他机型使用相同接口，但可用功能取决于机器与 BIOS 版本：触控板振动与边缘手势仅存在于压感触控板机型，部分机器可读取的传感器较少。固件不响应的项目不会出现在菜单中。

## 安装与运行

1. 从[最新发布版本](https://github.com/Wintego/Honor-PC-Helper/releases/latest)下载 `HonorPCHelper.exe`。
2. 直接运行 —— 无需安装、无需依赖。图标会出现在系统托盘中。
3. 点击托盘图标即可打开菜单。

### 何时需要管理员权限

硬件命令通过名为 **Honor PC Helper Privileged Hardware** 的计划任务执行：它以当前用户的最高权限启动同一个 exe，并执行一条待处理命令。该任务在你首次修改硬件设置时创建 —— 这是唯一一次 UAC 提示。此后所有修改（包括读取传感器）都经由该任务完成，不再需要提权。

左边缘的亮度手势还需要对 ACPI-WMI 数据块 `abbc0f5b-8ea1-11d1-a000-c90629100000`（`HKLM\SYSTEM\CurrentControlSet\Control\WMI\Security`）授予一次性权限，该数据块默认仅管理员可调用。首次使用时由上述计划任务为你的账户授权；在此之前亮度将通过备用方式 `WmiSetBrightness` 调整——每次 3%，但不会显示 Windows 原生 OSD。

## 驱动程序

托盘菜单中的**驱动程序**会打开一个窗口，显示 BIOS 版本以及驱动与软件列表。设备清单在程序启动时于后台收集，因此窗口打开时列表已经填好。

- 通过 BIOS 中的 `DeviceTypeEx`/`CVersion`、主板与产品标识、处理器型号和内存容量，将机型与荣耀目录进行匹配。
- 软件包来自荣耀更新平台（`update.platform.hihonorcloud.com`），并以官方支持目录（`selfservice-ap/eu/cn.honor.com`）作为备选。返回 404/410 的链接会被剔除，因此失效条目不会被当作更新显示。
- 绿色表示已安装版本与提供的版本一致，红色表示有更新，灰色表示无法确定本地版本。只有两个版本号确实可比较时才判定为更新：构建日期不会与驱动版本相比较。
- 点击版本即可下载并校验软件包，然后保存到你指定的位置：服务器提供时校验 SHA-256，对每个 `.exe` 校验数字签名；若文件并非来自荣耀官方主机，还要求签名者为 Honor/Huawei。压缩包解压时带有路径穿越防护。
- **不会自动运行安装程序。** 你得到的是一个已校验的文件，是否运行由你决定。
- **驱动程序的导出与导入。** 列表标题栏中的按钮可将系统中所有第三方驱动程序保存为一个 zip（`pnputil /export-driver`），也可以从这样的存档或单个 `.inf` 重新安装（`pnputil /add-driver … /subdirs /install`）。两项操作都在子进程中执行，每次操作通过 UAC 提权一次；导入完成后列表会重新收集，若需要重启会另行提示。重装 Windows 之前导出的存档尤其有用：它不依赖荣耀目录是否仍为该机型提供软件包。

同一窗口还会显示 Honor PC Helper 的版本，并在有新版本时提供更新。

## 应用更新

打开驱动程序窗口时，应用会检查 GitHub Releases。更新下载后会与发布文件大小、GitHub 公布的 `sha256` 摘要、PE 头以及版本资源进行核对。随后将正在运行的 exe 改名，让新版本就位并以普通用户身份启动 —— 不会弹出管理员提示，除非该 exe 位于 `Program Files` 之类不可写的目录。若任何环节失败，会还原原先的版本。残留文件在下次启动时清除。

## 数据存放位置

| 内容 | 位置 |
| --- | --- |
| 设置与状态缓存 | `HKCU\Software\HonorPCHelper` |
| 开机自启动项 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `HonorPCHelper` |
| 日志（1 MB，轮转为 `.1`） | `%LocalAppData%\HonorPCHelper\HonorPCHelper.log` |
| 下载文件（下次启动时删除） | `%LocalAppData%\HonorPCHelper\DriverUpdates`、`…\AppUpdates` |
| 特权计划任务 | 任务计划程序 → `Honor PC Helper Privileged Hardware` |

充电阈值和背光超时由固件自身保存，而非本程序保存。

## 命令行参数

不带参数运行时程序作为托盘图标运行。其余参数供内部调用使用，此处一并说明，手动执行同样有效：

| 参数 | 用途 |
| --- | --- |
| `--set-battery-mode <Disabled\|Home\|Office\|Travel>` | 提权应用该设置并注册特权计划任务 |
| `--set-keyboard-backlight <Off\|Low\|High>` | 同上，用于背光亮度 |
| `--set-keyboard-backlight-timeout <秒>` | 同上，用于熄灭超时（0 表示不关闭） |
| `--set-power-unlock <true\|false>` | 同上，用于高性能模式 |
| `--apply-…` | 同样这四项设置，由特权任务静默执行 |
| `--install-privileged-tasks` | 仅创建特权计划任务，不改动任何设置 |
| `--uninstall-privileged-tasks` | 删除该任务（需在管理员命令行中运行） |
| `--export-drivers <存档.zip>` | 将驱动程序存储导出为存档（需要管理员权限） |
| `--import-drivers <存档.zip\|文件夹\|文件.inf>` | 将驱动程序加入存储并安装到设备上 |
| `--restart-after <pid>` | 供自更新使用：等待旧进程退出后启动托盘 |

退出码：`0` 成功，`1` 失败，`2` 参数值无法解析。

## 疑难解答

**菜单中缺少部分项目。** 说明这些功能未被该机型的 BIOS 或触控板暴露。温度与风扇读数、振动反馈与边缘手势因机型和固件版本而异。

**设置没有生效。** 查看 `%LocalAppData%\HonorPCHelper\HonorPCHelper.log`：每一条被拒绝的 BIOS 命令都会连同错误码一起记录。如果计划任务被删除或 exe 被移动，下一次修改会重新注册它，并弹出一次 UAC 提示。

**Fn+P 没有反映在菜单中。** 应用监听 `OemWMIEvent` 事件；若订阅未能启动，原因会记录在日志中。

**左边缘滑动能调亮度，但没有 OSD。** 说明尚未授予 ACPI-WMI 权限：先修改任意一项硬件设置以创建特权任务，然后再次滑动。

**驱动检查没有任何结果。** 某些 VPN 与代理客户端会残留失效的 WinINET 代理设置，因此本程序忽略系统代理；但防火墙仍可能拦截 `hihonorcloud.com` 与 `honor.com`。

## 卸载

1. 取消勾选**开机自启动**，然后选择**退出**。
2. 在管理员命令行中执行：`HonorPCHelper.exe --uninstall-privileged-tasks`。
3. 删除 `HKCU\Software\HonorPCHelper`、`%LocalAppData%\HonorPCHelper` 以及 exe 文件。

若希望电池恢复充至 100%，请在卸载前将**充电限制**设为**关闭**：阈值保存在电源控制器中，不会随程序一起消失。

## 常见问题

**有没有轻量级的荣耀电脑管家替代方案？** 本项目就是。它以单个绿色 exe 覆盖硬件设置与驱动更新，免安装、无后台服务、无需厂商账号。它不提供手机多屏协同（MagicRing）。

**可以卸载荣耀电脑管家只用这个工具吗？** 如果你不需要 MagicRing，可以。电脑管家写入的数值（充电阈值、背光超时）保存在 BIOS 中并继续生效；Honor PC Helper 通过同一接口读取它们。

**不用电脑管家如何限制电池充电？** 托盘菜单 → **充电限制** → 选择区间。阈值通过荣耀 BIOS 的 WMI 接口写入，重启后依然有效。

**需要管理员权限吗？** 只需一次，用于创建执行硬件命令的计划任务。日常使用（包括更新）均无需提权。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)：

```powershell
.\build.ps1
```

产物是单个自包含并经过压缩的 `dist\HonorPCHelper.exe`。同一目录树中还包含 `Assets\`、`.github\workflows\build.yml`（CI 构建并在 `v*` 标签上发布）以及 `RELEASE_NOTES.md`（发布说明正文）。

---

本项目与荣耀（HONOR）无关，未获其认可或支持。HONOR 与 MagicBook 为各自所有者的商标。
