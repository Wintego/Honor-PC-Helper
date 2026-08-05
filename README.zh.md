# Honor PC Helper

[English](README.md) | [Русский](README.ru.md)

用于从 Windows 系统托盘管理 HONOR 笔记本硬件功能的小工具。基于 HONOR 官方 WMI 接口。

## 功能

- **电池** — 限制充电区间（延长电池寿命）
- **键盘背光** — 开启/关闭、熄灭超时、自动开启计划
- **性能** — 在智能模式与高性能模式之间切换
- **监控** — 在托盘提示中显示温度、风扇转速和硬件设置
- **触控板** — 振动反馈强度、边缘手势（左侧调亮度，右侧调音量）、沿左边缘滑动调节屏幕亮度
- **快捷键** — 窗口和多媒体的全局快捷键，可在托盘菜单中重新分配
- **开机自启动** — 随 Windows 一起启动
- **界面语言** — 俄语、英语和简体中文，根据 Windows 显示语言自动选择

## 截图

![Honor PC Helper](Assets/Screenshot-en.png)

## 使用方法

从[最新发布版本](https://github.com/Wintego/honor-pc-helper/releases/latest)下载 `HonorPCHelper.exe` 并运行，无需安装。

首次修改硬件设置时，Windows 会请求管理员权限以创建计划任务。

程序面向 **Windows x64**。可用功能取决于 HONOR 笔记本型号。

## 快捷键

默认值：`Alt+M` — 最小化光标所在窗口，`Alt+X` — 播放/暂停，`Alt+C` — 下一曲，`Alt+Z` — 上一曲。

要修改快捷键，请在托盘菜单中点击对应项目并按下新的组合键。至少需要一个修饰键 — Ctrl、Alt 或 Win。`Esc` 取消输入，`Del` 停用该快捷键。修改立即生效并保存到注册表；“恢复默认快捷键”项可还原初始设置。

如果组合键已被其他程序占用，程序会通过气泡提示告知，并将该菜单项标记为“已被占用”。

## 配置

配置文件是可选的。如需修改默认值，请在 exe 同级目录创建 `config.json`：

```json
{
  "brightnessStepPercent": 5,
  "sensorRefreshIntervalMs": 5000,
  "touchpadBrightnessEnabled": true,
  "hotkeysEnabled": true
}
```

修改将在重启程序后生效。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)：

```powershell
.\build.ps1
```

生成的文件位于 `dist\HonorPCHelper.exe`。
