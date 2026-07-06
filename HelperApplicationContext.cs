namespace HonorPCHelper;

internal sealed class HelperApplicationContext : ApplicationContext
{
    private static readonly int SensorRefreshIntervalMilliseconds = AppConfig.Current.SensorRefreshIntervalMs;
    private const int MinimizeHotkeyId = 1;
    private const int PlayPauseHotkeyId = 2;
    private const int NextTrackHotkeyId = 3;
    private const int PreviousTrackHotkeyId = 4;
    private const uint ModAlt = 0x0001;
    private const uint VkC = 0x43;
    private const uint VkM = 0x4D;
    private const uint VkX = 0x58;
    private const uint VkZ = 0x5A;
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPreviousTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventKeyUp = 0x0002;

    private readonly HotkeyWindow _window;
    private readonly Control _uiDispatcher;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _trayHoverTimer;
    private readonly IntPtr _tooltipHandle;
    private readonly TouchpadBrightnessService? _touchpadService;
    private readonly PowerModeEventService? _powerModeEvents;
    private readonly BacklightScheduleService _backlightSchedule;
    private Icon? _trayIcon;
    private IntPtr _tooltipText;
    private bool _tooltipAdded;
    private bool _disposed;
    private int _sensorRefreshInProgress;
    private long _lastSensorRefresh;
    private long _suppressBacklightEventsUntil;
    private long _lastResumeRestore;
    private IntPtr _displayNotification;
    private IntPtr _hidNotification;
    private readonly System.Windows.Forms.Timer _deviceChangeTimer;
    private readonly List<int> _registeredHotkeyIds = [];
    private Point _lastTrayMousePosition;
    private const int ResumeSettleMilliseconds = 8000;
    private const int ResumeDebounceMilliseconds = 10000;

    internal HelperApplicationContext()
    {
        _window = new HotkeyWindow(OnHotkeyPressed, OnMenuTooltip, OnDisplayResume, OnHidDeviceChange);
        _uiDispatcher = new Control();
        _ = _uiDispatcher.Handle;
        _tooltipHandle = NativeMethods.CreateWindowEx(
            NativeMethods.WsExTopmost, "tooltips_class32", null,
            NativeMethods.WsPopup | NativeMethods.TtsNoPrefix | NativeMethods.TtsAlwaysTip,
            NativeMethods.CwUseDefault, NativeMethods.CwUseDefault,
            NativeMethods.CwUseDefault, NativeMethods.CwUseDefault,
            _window.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmSetMaxTipWidth, 0, 300);
        NativeMethods.SetWindowPos(
            _tooltipHandle,
            NativeMethods.HwndTopmost,
            0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        _backlightSchedule = new BacklightScheduleService();
        _trayIcon = TrayIconFactory.Create(HardwareSettings.PerformanceModeActive);
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = DiagnosticsService.BuildCompactToolTip(),
            Visible = true
        };
        _notifyIcon.MouseMove += OnTrayIconMouseMove;
        _notifyIcon.MouseClick += OnTrayIconMouseClick;
        _trayHoverTimer = new System.Windows.Forms.Timer
        {
            Interval = SensorRefreshIntervalMilliseconds
        };
        _trayHoverTimer.Tick += OnTrayHoverTimerTick;
        _deviceChangeTimer = new System.Windows.Forms.Timer { Interval = 1_000 };
        _deviceChangeTimer.Tick += OnDeviceChangeTimerTick;

        RegisterHotkeys();

        if (AppConfig.Current.TouchpadBrightnessEnabled)
        {
            _touchpadService = new TouchpadBrightnessService(ShowError);
            _touchpadService.Start();
            _hidNotification = NativeMethods.RegisterHidDeviceNotification(_window.Handle);
        }
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _displayNotification = NativeMethods.RegisterPowerSettingNotification(
            _window.Handle, NativeMethods.GuidConsoleDisplayState, NativeMethods.DeviceNotifyWindowHandle);

        try
        {
            _powerModeEvents = new PowerModeEventService(
                HandlePowerModeChanged,
                HandleKeyboardBacklightChanged,
                ShouldIgnoreBacklightEvent);
            _powerModeEvents.Start();
        }
        catch (Exception exception)
        {
            _powerModeEvents?.Dispose();
            AppLog.Error("Could not start HONOR WMI event monitoring", exception);
            ShowError(L.T(
                $"Не удалось отслеживать Fn+P: {exception.Message}",
                $"Failed to monitor Fn+P: {exception.Message}"));
        }

        _backlightSchedule.Start();
        _ = RefreshSensorsAsync();
    }

    protected override void Dispose(bool disposing)
    {
        UnregisterHotkeys();
        if (disposing)
        {
            _disposed = true;
            _touchpadService?.Dispose();
            _powerModeEvents?.Dispose();
            _backlightSchedule.Dispose();
            HideNativeTooltip();
            if (_tooltipHandle != IntPtr.Zero)
                NativeMethods.DestroyWindow(_tooltipHandle);
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (_displayNotification != IntPtr.Zero)
            {
                NativeMethods.UnregisterPowerSettingNotification(_displayNotification);
                _displayNotification = IntPtr.Zero;
            }
            if (_hidNotification != IntPtr.Zero)
            {
                NativeMethods.UnregisterDeviceNotification(_hidNotification);
                _hidNotification = IntPtr.Zero;
            }
            _uiDispatcher.Dispose();
            _trayHoverTimer.Dispose();
            _deviceChangeTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayIcon?.Dispose();
            _window.DestroyHandle();
        }
        base.Dispose(disposing);
    }

    private NativePopupMenu CreateMenu()
    {
        var menu = new NativePopupMenu();

        var batterySub = menu.AddSubMenu(
            L.T("Ограничение заряда", "Charge limit"),
            L.T("Ограничение диапазона заряда для продления срока службы батареи.",
                "Limit the charge range to extend battery lifespan."));
        BatteryProtectionMenu.Build(batterySub);

        var backlightSub = menu.AddSubMenu(
            L.T("Подсветка клавиатуры", "Keyboard backlight"),
            L.T("Уровень подсветки и автоматическое расписание.",
                "Backlight level and automatic schedule."));
        KeyboardBacklightMenu.Build(backlightSub, _backlightSchedule);

        PowerUnlockMenu.Build(menu, UpdateTrayIcon);

        if (_registeredHotkeyIds.Count > 0)
        {
            menu.AddItem(L.T("Alt+M: свернуть окно под курсором", "Alt+M: minimize window under cursor"), null, enabled: false);
            menu.AddItem(L.T("Alt+X: play/pause", "Alt+X: play/pause"), null, enabled: false);
            menu.AddItem(L.T("Alt+C: следующий трек", "Alt+C: next track"), null, enabled: false);
            menu.AddItem(L.T("Alt+Z: предыдущий трек", "Alt+Z: previous track"), null, enabled: false);
        }
        if (AppConfig.Current.TouchpadBrightnessEnabled)
            menu.AddItem(L.T("Левый край тачпада: яркость", "Left edge of touchpad: brightness"), null, enabled: false);
        menu.AddSeparator();
        menu.AddItem(
            L.T("Запускать вместе с Windows", "Start with Windows"),
            ToggleStartup,
            @checked: StartupManager.IsEnabled);
        menu.AddItem(L.T("Выход", "Exit"), ExitThread);

        return menu;
    }

    private void ToggleStartup()
    {
        try
        {
            StartupManager.SetEnabled(!StartupManager.IsEnabled);
        }
        catch (Exception exception)
        {
            MessageBox.Show(L.T(
                    $"Не удалось изменить автозапуск: {exception.Message}",
                    $"Failed to change startup setting: {exception.Message}"),
                "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Регистрирует каждое сочетание отдельно: занятое другим приложением
    // не мешает остальным и не приводит к выходу.
    private void RegisterHotkeys()
    {
        if (!AppConfig.Current.HotkeysEnabled)
            return;

        (int Id, uint Vk, string Label)[] hotkeys =
        [
            (MinimizeHotkeyId, VkM, "Alt+M"),
            (PlayPauseHotkeyId, VkX, "Alt+X"),
            (NextTrackHotkeyId, VkC, "Alt+C"),
            (PreviousTrackHotkeyId, VkZ, "Alt+Z")
        ];

        var failed = new List<string>();
        foreach (var (id, vk, label) in hotkeys)
        {
            if (NativeMethods.RegisterHotKey(_window.Handle, id, ModAlt, vk))
                _registeredHotkeyIds.Add(id);
            else
                failed.Add(label);
        }

        if (failed.Count == 0)
            return;

        var list = string.Join(", ", failed);
        AppLog.Error($"Hotkeys already in use: {list}");
        _notifyIcon.ShowBalloonTip(5_000, "Honor PC Helper",
            L.T($"Сочетания заняты другим приложением и отключены: {list}",
                $"Shortcuts are taken by another application and disabled: {list}"),
            ToolTipIcon.Warning);
    }

    private void UnregisterHotkeys()
    {
        foreach (var id in _registeredHotkeyIds)
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        _registeredHotkeyIds.Clear();
    }

    private static void OnHotkeyPressed(int hotkeyId)
    {
        switch (hotkeyId)
        {
            case MinimizeHotkeyId:
                MinimizeWindowUnderCursor();
                break;
            case PlayPauseHotkeyId:
                SendMediaKey(VkMediaPlayPause);
                break;
            case NextTrackHotkeyId:
                SendMediaKey(VkMediaNextTrack);
                break;
            case PreviousTrackHotkeyId:
                SendMediaKey(VkMediaPreviousTrack);
                break;
        }
    }

    private static void MinimizeWindowUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return;

        var target = NativeMethods.WindowFromPoint(point);
        if (target == IntPtr.Zero)
            return;

        var root = NativeMethods.GetAncestor(target, NativeMethods.GetAncestorFlags.GetRoot);
        if (root != IntPtr.Zero)
            target = root;

        if (target != NativeMethods.GetDesktopWindow() && target != NativeMethods.GetShellWindow())
            NativeMethods.ShowWindow(target, NativeMethods.ShowWindowCommands.Minimize);
    }

    private static void SendMediaKey(byte virtualKey)
    {
        NativeMethods.KeybdEvent(virtualKey, 0, 0, UIntPtr.Zero);
        NativeMethods.KeybdEvent(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private void ShowError(string message)
    {
        if (_disposed)
            return;

        AppLog.Error(message);
    }

    private async void ApplyPowerModeChange(bool enabled)
    {
        if (_disposed)
            return;

        if (enabled && !PerformanceModePolicy.CanEnable(out var reason))
        {
            await DisablePerformanceModeAsync();
            ShowError(reason);
            return;
        }

        UpdateTrayIcon();
    }

    private void HandlePowerModeChanged(bool enabled)
    {
        if (_disposed || _uiDispatcher.IsDisposed || _tooltipHandle == IntPtr.Zero)
            return;

        try
        {
            _uiDispatcher.BeginInvoke(() => ApplyPowerModeChange(enabled));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool ShouldIgnoreBacklightEvent()
        => Environment.TickCount64 < Interlocked.Read(ref _suppressBacklightEventsUntil);

    // Вызывается из WndProc (UI-поток) при подключении/отключении HID-устройства.
    // Дебаунс таймером: система шлёт пачку событий на одно физическое подключение.
    private void OnHidDeviceChange(string devicePath)
    {
        if (_disposed || _touchpadService is null || !TouchpadBrightnessService.IsSupportedDevicePath(devicePath))
            return;

        _deviceChangeTimer.Stop();
        _deviceChangeTimer.Start();
    }

    private void OnDeviceChangeTimerTick(object? sender, EventArgs eventArgs)
    {
        _deviceChangeTimer.Stop();
        if (!_disposed)
            _touchpadService?.Restart();
    }

    // On modern standby (S0) systems the classic Resume power event is unreliable;
    // the display turning back on is the dependable wake signal.
    private void OnDisplayResume()
    {
        if (_disposed)
            return;

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastResumeRestore) < ResumeDebounceMilliseconds)
            return;

        Interlocked.Exchange(ref _lastResumeRestore, now);
        Interlocked.Exchange(ref _suppressBacklightEventsUntil, now + ResumeSettleMilliseconds);
        AppLog.Info("Display turned on, restoring keyboard backlight");
        _ = _backlightSchedule.RestoreAfterResumeAsync();
    }

    private void HandleKeyboardBacklightChanged(KeyboardBacklightLevel level)
    {
        if (_disposed || _uiDispatcher.IsDisposed)
            return;

        try
        {
            _uiDispatcher.BeginInvoke(() =>
            {
                HardwareSettings.KeyboardBacklight = level;
                _backlightSchedule.SetManualOverride();
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnTrayIconMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (_disposed)
            return;

        try
        {
            _lastTrayMousePosition = Control.MousePosition;
            _trayHoverTimer.Start();
            _notifyIcon.Text = DiagnosticsService.BuildCompactToolTip();
            _ = RefreshSensorsAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error("Tray tooltip update failed", exception);
        }
    }

    private void OnTrayHoverTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_disposed || Control.MousePosition != _lastTrayMousePosition)
        {
            _trayHoverTimer.Stop();
            return;
        }

        _notifyIcon.Text = DiagnosticsService.BuildCompactToolTip();
        _ = RefreshSensorsAsync();
    }

    private async Task RefreshSensorsAsync()
    {
        if (_disposed || Environment.TickCount64 - Interlocked.Read(ref _lastSensorRefresh) < SensorRefreshIntervalMilliseconds
            || Interlocked.Exchange(ref _sensorRefreshInProgress, 1) != 0)
            return;

        Interlocked.Exchange(ref _lastSensorRefresh, Environment.TickCount64);
        try
        {
            if (!await PrivilegedHardware.TryReadSensorsTaskAsync() || _disposed || _uiDispatcher.IsDisposed)
                return;

            _uiDispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                    _notifyIcon.Text = DiagnosticsService.BuildCompactToolTip();
            });
        }
        catch (Exception exception)
        {
            AppLog.Error("Hardware sensor refresh failed", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _sensorRefreshInProgress, 0);
        }
    }

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        if (_disposed)
            return;

        if (eventArgs.Button == MouseButtons.Right)
        {
            using var menu = CreateMenu();
            var commandId = menu.Show(_window.Handle);
            HideNativeTooltip();
            menu.TryInvoke(commandId);
        }
    }

    private void OnMenuTooltip(string? text)
    {
        if (_disposed || _uiDispatcher.IsDisposed)
            return;

        if (text == null)
        {
            HideNativeTooltip();
            return;
        }

        var mousePos = Control.MousePosition;
        var textPtr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(text);
        var previousText = _tooltipText;
        _tooltipText = textPtr;
        var ti = new NativeMethods.ToolInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ToolInfo>(),
            Flags = NativeMethods.TtfIdIsHwnd | NativeMethods.TtfTrack | NativeMethods.TtfAbsolute | NativeMethods.TtfTransparent,
            Window = _window.Handle,
            Id = _window.Handle,
            Text = textPtr
        };
        if (_tooltipAdded)
            NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmUpdateTipTextW, 0, ref ti);
        else
        {
            NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmAddToolW, 0, ref ti);
            _tooltipAdded = true;
        }
        var pos = ((mousePos.Y + 20) << 16) | ((mousePos.X + 16) & 0xFFFF);
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmTrackPosition, 0, pos);
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmTrackActivate, 1, ref ti);
        if (previousText != IntPtr.Zero)
            System.Runtime.InteropServices.Marshal.FreeHGlobal(previousText);
    }

    private void HideNativeTooltip()
    {
        if (_tooltipHandle == IntPtr.Zero || !_tooltipAdded)
            return;

        var ti = new NativeMethods.ToolInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ToolInfo>(),
            Flags = NativeMethods.TtfIdIsHwnd | NativeMethods.TtfTrack | NativeMethods.TtfAbsolute | NativeMethods.TtfTransparent,
            Window = _window.Handle,
            Id = _window.Handle,
            Text = _tooltipText
        };
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmTrackActivate, 0, ref ti);
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmDelToolW, 0, ref ti);
        _tooltipAdded = false;
        if (_tooltipText != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(_tooltipText);
            _tooltipText = IntPtr.Zero;
        }
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs eventArgs)
    {
        if (_disposed || _uiDispatcher.IsDisposed)
            return;

        try
        {
            _uiDispatcher.BeginInvoke(UpdateTheme);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void UpdateTheme() => UpdateTrayIcon();

    private void UpdateTrayIcon()
    {
        var previous = _trayIcon;
        _trayIcon = TrayIconFactory.Create(HardwareSettings.PerformanceModeActive);
        _notifyIcon.Icon = _trayIcon;
        previous?.Dispose();
    }

    private async void OnSystemPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            await DisablePerformanceModeAsync();
            return;
        }

        if (eventArgs.Mode == Microsoft.Win32.PowerModes.StatusChange
            && HardwareSettings.PerformanceModeActive
            && !PerformanceModePolicy.CanEnable(out _))
        {
            await DisablePerformanceModeAsync();
            return;
        }

        if (eventArgs.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            HardwareSettings.PowerUnlock = false;
            HardwareSettings.PerformanceModeActive = false;
            HandlePowerModeChanged(false);
            Interlocked.Exchange(ref _suppressBacklightEventsUntil, Environment.TickCount64 + ResumeSettleMilliseconds);
            _ = _backlightSchedule.RestoreAfterResumeAsync();
        }
    }

    private async Task DisablePerformanceModeAsync()
    {
        if (!HardwareSettings.PerformanceModeActive)
            return;

        if (!await PrivilegedHardware.TryRunPowerUnlockTaskAsync(false))
            return;
        HardwareSettings.PowerUnlock = false;
        HardwareSettings.PerformanceModeActive = false;
        HandlePowerModeChanged(false);
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private const int WmHotkey = 0x0312;
        private const int WmMenuSelect = 0x011F;
        private readonly Action<int> _onHotkeyPressed;
        private readonly Action<string?> _onTooltip;
        private readonly Action _onDisplayResume;
        private readonly Action<string> _onDeviceChange;

        internal HotkeyWindow(Action<int> onHotkeyPressed, Action<string?> onTooltip,
            Action onDisplayResume, Action<string> onDeviceChange)
        {
            _onHotkeyPressed = onHotkeyPressed;
            _onTooltip = onTooltip;
            _onDisplayResume = onDisplayResume;
            _onDeviceChange = onDeviceChange;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey)
            {
                _onHotkeyPressed(message.WParam.ToInt32());
                return;
            }
            if (message.Msg == NativeMethods.WmPowerBroadcast
                && message.WParam.ToInt32() == NativeMethods.PbtPowerSettingChange
                && message.LParam != IntPtr.Zero)
            {
                var setting = System.Runtime.InteropServices.Marshal
                    .PtrToStructure<NativeMethods.PowerBroadcastSetting>(message.LParam);
                if (setting.PowerSetting == NativeMethods.GuidConsoleDisplayState && setting.Data == 1)
                    _onDisplayResume();
                base.WndProc(ref message);
                return;
            }
            if (message.Msg == NativeMethods.WmDeviceChange && message.LParam != IntPtr.Zero)
            {
                var eventType = (int)message.WParam.ToInt64();
                if (eventType is NativeMethods.DbtDeviceArrival or NativeMethods.DbtDeviceRemoveComplete
                    && System.Runtime.InteropServices.Marshal.ReadInt32(message.LParam, 4)
                        == NativeMethods.DbtDevTypDeviceInterface)
                {
                    var devicePath = System.Runtime.InteropServices.Marshal.PtrToStringUni(
                        message.LParam + NativeMethods.DevBroadcastNameOffset);
                    if (!string.IsNullOrEmpty(devicePath))
                        _onDeviceChange(devicePath);
                }
                base.WndProc(ref message);
                return;
            }
            if (message.Msg == WmMenuSelect)
            {
                var commandId = (int)(message.WParam.ToInt64() & 0xFFFF);
                var flags = (uint)((message.WParam.ToInt64() >> 16) & 0xFFFF);
                var menuHandle = message.LParam;
                if (menuHandle == IntPtr.Zero || commandId == 0xFFFF)
                    _onTooltip(null);
                else if ((flags & NativeMethods.MfPopup) != 0)
                    _onTooltip(NativePopupMenu.GetSubMenuTooltip(menuHandle, commandId));
                else
                    _onTooltip(NativePopupMenu.GetTooltip(commandId));
            }
            base.WndProc(ref message);
        }
    }

}
