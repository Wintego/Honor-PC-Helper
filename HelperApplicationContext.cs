namespace HonorPCHelper;

internal sealed class HelperApplicationContext : ApplicationContext
{
    private const int SensorRefreshIntervalMilliseconds = 5_000;
    // Мышь над иконкой трея генерирует поток событий, а сборка подсказки читает
    // реестр и WMI - поэтому обновление ограничено по частоте.
    private const int TooltipMinIntervalMilliseconds = 750;
    // Оболочка сама решает, сколько держать уведомление; значение лишь
    // подсказывает, что сообщение короткое и задерживать его незачем.
    private const int NotificationTimeoutMilliseconds = 3_000;

    private readonly MessageWindow _window;
    private readonly Control _uiDispatcher;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _trayHoverTimer;
    private readonly IntPtr _tooltipHandle;
    private readonly TouchpadBrightnessService _touchpadService;
    private PowerModeEventService? _powerModeEvents;
    private readonly BacklightScheduleService _backlightSchedule;
    private readonly BatteryProtectionService _batteryProtection;
    private readonly MicMuteService _micMute;
    private readonly CameraMuteService _cameraMute;
    private readonly ApplicationUpdateWatcher _applicationUpdates;
    private Icon _trayIcon;
    private IntPtr _tooltipText;
    private bool _tooltipAdded;
    private bool _disposed;
    private int _sensorRefreshInProgress;
    // Отрицательные значения, чтобы вскоре после загрузки системы, когда
    // TickCount64 ещё мал, первый опрос датчиков и первое пробуждение экрана
    // не попадали под свои же интервалы подавления.
    private long _lastSensorRefresh = -SensorRefreshIntervalMilliseconds;
    private long _suppressBacklightEventsUntil;
    private long _lastResumeRestore = -ResumeDebounceMilliseconds;
    private IntPtr _displayNotification;
    private IntPtr _hidNotification;
    private readonly System.Windows.Forms.Timer _deviceChangeTimer;
    private Point _lastTrayMousePosition;
    private string _tooltipCache = string.Empty;
    private long _lastTooltipUpdate;
    private const int ResumeSettleMilliseconds = 8000;
    private const int ResumeDebounceMilliseconds = 10000;
    // Перечень драйверов (Win32_PnPSignedDriver) - самый тяжёлый запрос WMI
    // приложения. Сразу после входа в систему он соперничал бы с автозагрузкой,
    // поэтому собирается с задержкой - или сразу, если окно драйверов
    // открыли раньше.
    private static readonly TimeSpan DriverInventoryDelay = TimeSpan.FromMinutes(1);
    private readonly Lazy<Task<IReadOnlyList<DriverComponent>>> _driverComponents = new(StartDriverInventory);

    internal HelperApplicationContext()
    {
        _window = new MessageWindow(OnMenuTooltip, OnDisplayResume, OnHidDeviceChange);
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
        _batteryProtection = new BatteryProtectionService();
        _micMute = new MicMuteService();
        _cameraMute = new CameraMuteService();
        _applicationUpdates = new ApplicationUpdateWatcher();
        ApplicationUpdateWatcher.Changed += OnApplicationUpdateChanged;
        // Выбор, сделанный до появления отдельного значения в реестре,
        // переносится до первого опроса датчиков - иначе пороги, потерянные
        // прошивкой, успеют его перезаписать.
        HardwareSettings.SeedPreferredBatteryProtection();
        _trayIcon = TrayIconFactory.Create(
            HardwareSettings.PerformanceModeActive,
            ApplicationUpdateWatcher.Available is not null);
        // Подсказка обращается к WMI, поэтому при старте показывается название
        // приложения, а настоящий текст подставляется первым же обновлением:
        // значок в трее не должен ждать опроса датчиков.
        _tooltipCache = "Honor PC Helper";
        _lastTooltipUpdate = 0;
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = _tooltipCache,
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

        _touchpadService = new TouchpadBrightnessService(ShowError);
        _hidNotification = NativeMethods.RegisterHidDeviceNotification(_window.Handle);
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _displayNotification = NativeMethods.RegisterPowerSettingNotification(
            _window.Handle, NativeMethods.GuidConsoleDisplayState, NativeMethods.DeviceNotifyWindowHandle);

        _backlightSchedule.Start();
        _ = Task.Delay(DriverInventoryDelay).ContinueWith(
            _ => _disposed ? null : _driverComponents.Value,
            TaskScheduler.Default);
        _ = Task.Run(StartHardwareServices);
    }

    private static Task<IReadOnlyList<DriverComponent>> StartDriverInventory()
    {
        var task = new DriverUpdateService().BuildDeviceListAsync();
        _ = task.ContinueWith(
            completed => AppLog.Error("Background driver device inventory failed", completed.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        return task;
    }

    /// <summary>
    /// Всё, что обращается к WMI и HID, поднимается в фоне: перечисление
    /// HID-устройств и подключение к root\wmi занимают сотни миллисекунд,
    /// а значок в трее должен появляться сразу после запуска.
    /// </summary>
    private void StartHardwareServices()
    {
        if (_disposed)
            return;

        _touchpadService.Start();
        // Меню трея спрашивает о наличии тачпада при каждом открытии - пусть
        // ответ уже лежит в кэше к тому моменту, как пользователь щёлкнет значок.
        TouchpadHapticsController.IsSupported();
        // Уровень вибрации и жесты краёв живут в прошивке тачпада
        // и сбрасываются при перезагрузке.
        TouchpadHapticsController.Reapply();
        TouchpadGesturesController.Reapply();
        // Индикатор микрофона прошивка не восстанавливает: после перезагрузки
        // он мог остаться включённым при живом микрофоне.
        _micMute.Sync();

        try
        {
            var events = new PowerModeEventService(
                HandlePowerModeChanged,
                HandleKeyboardBacklightChanged,
                HandleMicMuteKey,
                HandleCameraKey,
                ShouldIgnoreBacklightEvent);
            events.Start();
            _powerModeEvents = events;
            // Приложение могло закрыться, пока служба поднималась.
            if (_disposed)
            {
                events.Dispose();
                return;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not start HONOR WMI event monitoring", exception);
            ShowError(L.T(
                $"Не удалось отслеживать Fn+P: {exception.Message}",
                $"Failed to monitor Fn+P: {exception.Message}",
                $"无法监听 Fn+P：{exception.Message}"));
        }

        _applicationUpdates.Start();

        // Право записи выключателя камеры выдаётся заранее и в стороне от
        // остального запуска: секунда на привилегированную задачу иначе
        // доставалась первому нажатию F8.
        _ = Task.Run(_cameraMute.Prepare);

        _ = RefreshSensorsAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _touchpadService.Dispose();
            _powerModeEvents?.Dispose();
            _backlightSchedule.Dispose();
            _batteryProtection.Dispose();
            _micMute.Dispose();
            ApplicationUpdateWatcher.Changed -= OnApplicationUpdateChanged;
            _applicationUpdates.Dispose();
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
            _window.DestroyHandle();
        }
        base.Dispose(disposing);
    }

    private NativePopupMenu CreateMenu()
    {
        var menu = new NativePopupMenu();

        var batterySub = menu.AddSubMenu(
            L.T("Ограничение заряда", "Charge limit", "充电限制"),
            L.T("Ограничение диапазона заряда для продления срока службы батареи.",
                "Limit the charge range to extend battery lifespan.",
                "限制充电区间，延长电池寿命。"));
        BatteryProtectionMenu.Build(batterySub);

        var backlightSub = menu.AddSubMenu(
            L.T("Клавиатура", "Keyboard", "键盘"),
            L.T("Уровень подсветки, таймаут и автоматическое расписание.",
                "Backlight level, timeout and automatic schedule.",
                "背光亮度、超时时间和自动计划。"));
        KeyboardBacklightMenu.Build(backlightSub, _backlightSchedule);

        if (TouchpadHapticsController.IsSupported())
        {
            var touchpadSub = menu.AddSubMenu(
                L.T("Тачпад", "Touchpad", "触控板"),
                L.T("Интенсивность виброотклика и жесты на краях тачпада.",
                    "Vibration strength and touchpad edge gestures.",
                    "振动反馈强度和触控板边缘手势。"));
            TouchpadMenu.Build(touchpadSub);
        }

        PowerUnlockMenu.Build(menu, UpdateTrayIcon);

        if (ApplicationUpdateWatcher.Available is { } applicationUpdate)
        {
            var version = applicationUpdate.Version.ToString(3);
            menu.AddItem(
                L.T($"Обновить до {version}", $"Update to {version}", $"更新到 {version}"),
                () => InstallApplicationUpdate(applicationUpdate),
                tooltip: L.T(
                    $"Скачать и установить Honor PC Helper {version}. Приложение перезапустится.",
                    $"Download and install Honor PC Helper {version}. The app will restart.",
                    $"下载并安装 Honor PC Helper {version}。应用程序将重新启动。"));
        }

        menu.AddItem(
            L.T("Драйвера", "Drivers", "驱动程序"),
            () => ShowDriverManager(),
            tooltip: L.T(
                "Проверка и установка драйверов и прошивок с сервера HONOR.",
                "Check and install drivers and firmware from HONOR.",
                "从 HONOR 检查并安装驱动程序和固件。"));
        menu.AddSeparator();
        menu.AddItem(
            L.T("Запускать вместе с Windows", "Start with Windows", "开机自启动"),
            ToggleStartup,
            @checked: StartupManager.IsEnabled);
        menu.AddItem(L.T("Выход", "Exit", "退出"), ExitThread);

        return menu;
    }

    private DriverManagerForm ShowDriverManager()
    {
        var form = Application.OpenForms.OfType<DriverManagerForm>().FirstOrDefault();
        if (form is null || form.IsDisposed)
        {
            form = new DriverManagerForm(_driverComponents.Value);
            form.Show();
            form.Activate();
            form.BringToFront();
        }
        else
        {
            form.WindowState = FormWindowState.Normal;
            form.Activate();
            form.BringToFront();
        }
        return form;
    }

    /// <summary>
    /// Обновление из трея идёт через окно драйверов: там уже есть и строка
    /// приложения, и полоса загрузки, поэтому пункт меню открывает окно и
    /// сразу запускает установку.
    /// </summary>
    private void InstallApplicationUpdate(ApplicationUpdate update)
        => ShowDriverManager().RequestApplicationUpdate(update);

    private void OnApplicationUpdateChanged()
    {
        if (_disposed || _uiDispatcher.IsDisposed)
            return;

        try
        {
            _uiDispatcher.BeginInvoke(UpdateTrayIcon);
        }
        catch (InvalidOperationException)
        {
        }
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
                    $"Failed to change startup setting: {exception.Message}",
                    $"无法修改开机自启动设置：{exception.Message}"),
                "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
            // Режим включил сам человек клавишами Fn+P, поэтому один запрос UAC
            // здесь допустим - но не после того, как он уже отказал.
            await DisablePerformanceModeAsync(Elevation.UnlessDeclined);
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
        if (_disposed || !TouchpadBrightnessService.IsSupportedDevicePath(devicePath))
            return;

        _deviceChangeTimer.Stop();
        _deviceChangeTimer.Start();
    }

    private void OnDeviceChangeTimerTick(object? sender, EventArgs eventArgs)
    {
        _deviceChangeTimer.Stop();
        if (_disposed)
            return;

        // Путь устройства, найденный до переподключения, больше не действителен.
        TouchpadVendorLink.InvalidateCache();
        _touchpadService.Restart();
        // Прошивка тачпада забывает свои настройки при переподключении.
        TouchpadHapticsController.Reapply();
        TouchpadGesturesController.Reapply();
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
        _ = _batteryProtection.RestoreAfterResumeAsync();
        TouchpadHapticsController.Reapply();
        TouchpadGesturesController.Reapply();
        // Индикатор микрофона прошивка после сна не восстанавливает.
        _ = Task.Run(_micMute.Sync);
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

    // Клавиша F7. Обращение к звуковому движку и к BIOS занимает заметное время,
    // а поток события WMI держать нельзя: он общий на все события прошивки.
    private void HandleMicMuteKey()
    {
        if (_disposed)
            return;

        _ = Task.Run(() =>
        {
            if (_micMute.Toggle() is { } muted)
                ShowMicrophoneNotification(muted);
        });
    }

    /// <summary>
    /// Лампочка на клавише видна только за клавиатурой, а сам звук пропадает
    /// молча, - поэтому о переключении, как и у камеры, сообщает уведомление
    /// оболочки.
    /// </summary>
    private void ShowMicrophoneNotification(bool muted)
    {
        ShowNotification(
            muted
                ? L.T("Микрофон выключен", "Microphone is off", "麦克风已关闭")
                : L.T("Микрофон включён", "Microphone is on", "麦克风已开启"),
            muted
                ? L.T("Приложения не слышат звук.",
                    "Apps receive no sound.",
                    "应用程序听不到声音。")
                : L.T("Звук снова доступен приложениям.",
                    "Sound is available to apps again.",
                    "应用程序可以再次获取声音。"));
    }

    // Клавиша F8. Запись в реестр и, при первом нажатии, запуск привилегированной
    // задачи - поток события WMI на это занимать нельзя.
    private void HandleCameraKey()
    {
        if (_disposed)
            return;

        _ = Task.Run(() =>
        {
            if (_cameraMute.Toggle() is { } off)
                ShowCameraNotification(off);
        });
    }

    /// <summary>
    /// Состояние камеры нигде не видно - ни лампочки на клавише, ни индикатора
    /// в системе, - поэтому о переключении сообщает уведомление оболочки.
    /// </summary>
    private void ShowCameraNotification(bool off)
    {
        ShowNotification(
            off
                ? L.T("Камера выключена", "Camera is off", "摄像头已关闭")
                : L.T("Камера включена", "Camera is on", "摄像头已开启"),
            off
                ? L.T("Приложения получают чёрный кадр.",
                    "Apps receive a black frame.",
                    "应用程序只会收到黑屏画面。")
                : L.T("Изображение снова доступно приложениям.",
                    "Video is available to apps again.",
                    "应用程序可以再次获取画面。"));
    }

    // Уведомление показывает значок в трее, а он живёт в потоке интерфейса.
    private void ShowNotification(string title, string text)
    {
        if (_disposed || _uiDispatcher.IsDisposed)
            return;

        try
        {
            _uiDispatcher.BeginInvoke(() =>
            {
                if (_disposed)
                    return;

                _notifyIcon.ShowBalloonTip(
                    NotificationTimeoutMilliseconds,
                    title,
                    text,
                    ToolTipIcon.Info);
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
            if (!_trayHoverTimer.Enabled)
                _trayHoverTimer.Start();
            UpdateTrayTooltip();
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

        UpdateTrayTooltip();
        _ = RefreshSensorsAsync();
    }

    /// <summary>
    /// Пересобирает подсказку не чаще, чем раз в <see cref="TooltipMinIntervalMilliseconds"/>,
    /// и обращается к оболочке только когда текст действительно изменился.
    /// </summary>
    private void UpdateTrayTooltip(bool force = false)
    {
        if (_disposed)
            return;

        var now = Environment.TickCount64;
        if (!force && now - _lastTooltipUpdate < TooltipMinIntervalMilliseconds)
            return;

        _lastTooltipUpdate = now;
        var text = DiagnosticsService.BuildCompactToolTip();
        if (text == _tooltipCache)
            return;

        _tooltipCache = text;
        _notifyIcon.Text = text;
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

            _uiDispatcher.BeginInvoke(() => UpdateTrayTooltip(force: true));
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
            Action? action;
            using var menu = CreateMenu();
            var commandId = menu.Show(_window.Handle);
            HideNativeTooltip();
            action = menu.GetCallback(commandId);
            if (action is not null)
                _uiDispatcher.BeginInvoke(action);
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
        var icon = TrayIconFactory.Create(
            HardwareSettings.PerformanceModeActive,
            ApplicationUpdateWatcher.Available is not null);
        if (ReferenceEquals(icon, _trayIcon))
            return;

        _trayIcon = icon;
        _notifyIcon.Icon = icon;
    }

    private async void OnSystemPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs eventArgs)
    {
        // Уход в сон и отключение зарядки человек с приложением не связывает:
        // окно UAC здесь недопустимо, режим снимается только через фоновую задачу.
        if (eventArgs.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            await DisablePerformanceModeAsync(Elevation.Never);
            return;
        }

        if (eventArgs.Mode == Microsoft.Win32.PowerModes.StatusChange
            && HardwareSettings.PerformanceModeActive
            && !PerformanceModePolicy.CanEnable(out _))
        {
            await DisablePerformanceModeAsync(Elevation.Never);
            return;
        }

        if (eventArgs.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            HardwareSettings.PowerUnlock = false;
            HardwareSettings.PerformanceModeActive = false;
            HandlePowerModeChanged(false);
            Interlocked.Exchange(ref _suppressBacklightEventsUntil, Environment.TickCount64 + ResumeSettleMilliseconds);
            _ = _backlightSchedule.RestoreAfterResumeAsync();
            _ = _batteryProtection.RestoreAfterResumeAsync();
            _ = Task.Run(_micMute.Sync);
        }
    }

    /// <summary>
    /// Вызывается из async void обработчиков, поэтому ошибки не выпускает:
    /// исключение оттуда завершило бы процесс.
    /// </summary>
    private async Task DisablePerformanceModeAsync(Elevation elevation)
    {
        if (!HardwareSettings.PerformanceModeActive)
            return;

        try
        {
            if (!await PrivilegedHardware.TryRunPowerUnlockTaskAsync(false, elevation))
                return;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not leave performance mode", exception);
            return;
        }
        HardwareSettings.PowerUnlock = false;
        HardwareSettings.PerformanceModeActive = false;
        HandlePowerModeChanged(false);
    }

    private sealed class MessageWindow : NativeWindow
    {
        private const int WmMenuSelect = 0x011F;
        private readonly Action<string?> _onTooltip;
        private readonly Action _onDisplayResume;
        private readonly Action<string> _onDeviceChange;

        internal MessageWindow(Action<string?> onTooltip,
            Action onDisplayResume, Action<string> onDeviceChange)
        {
            _onTooltip = onTooltip;
            _onDisplayResume = onDisplayResume;
            _onDeviceChange = onDeviceChange;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message message)
        {
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
