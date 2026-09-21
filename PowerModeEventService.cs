using System.Management;

namespace HonorPCHelper;

internal sealed class PowerModeEventService : IDisposable
{
    // Клавиша микрофона; код совпадает с KEY_MICMUTE из huawei-wmi.
    private const uint MicMuteKeyCode = 0x287;

    // Клавиша камеры. Код снят с MagicBook Pro 14: huawei-wmi отдаёт его
    // как KEY_WLAN, но на этой клавиатуре F8 - именно камера.
    private const uint CameraKeyCode = 0x288;

    // Клавиша переключает состояние, поэтому лишнее событие обошлось бы дорого.
    // На проверенной машине одно нажатие даёт ровно одно событие, но у другой
    // прошивки это не гарантировано. Порог подавляющий - отсчёт продлевается
    // и погашенным событием, - поэтому пачка любой длины даёт ровно одно
    // переключение, по первому событию и без задержки.
    private const int HotkeyDebounceMilliseconds = 400;

    private readonly Action<bool> _onModeChanged;
    private readonly Action<KeyboardBacklightLevel> _onBacklightChanged;
    private readonly Action _onMicMuteKey;
    private readonly Action _onCameraKey;
    private readonly Func<bool>? _shouldIgnoreBacklightEvent;
    private ManagementEventWatcher? _watcher;
    private long _lastEventTime;
    // Отрицательные значения, чтобы вскоре после загрузки системы, когда
    // TickCount64 ещё мал, первое нажатие не попадало под собственный порог.
    private long _lastMicMuteKeyTime = -HotkeyDebounceMilliseconds;
    private long _lastCameraKeyTime = -HotkeyDebounceMilliseconds;
    private volatile bool _currentState = HardwareSettings.PerformanceModeActive;

    internal PowerModeEventService(
        Action<bool> onModeChanged,
        Action<KeyboardBacklightLevel> onBacklightChanged,
        Action onMicMuteKey,
        Action onCameraKey,
        Func<bool>? shouldIgnoreBacklightEvent = null)
    {
        _onModeChanged = onModeChanged;
        _onBacklightChanged = onBacklightChanged;
        _onMicMuteKey = onMicMuteKey;
        _onCameraKey = onCameraKey;
        _shouldIgnoreBacklightEvent = shouldIgnoreBacklightEvent;
    }

    internal void Start()
    {
        var scope = new ManagementScope(@"\\.\root\wmi");
        scope.Connect();
        _watcher = new ManagementEventWatcher(scope, new WqlEventQuery("SELECT * FROM OemWMIEvent"));
        _watcher.EventArrived += OnEventArrived;
        _watcher.Start();
    }

    public void Dispose()
    {
        if (_watcher is null)
            return;

        _watcher.EventArrived -= OnEventArrived;
        try
        {
            _watcher.Stop();
        }
        catch (ManagementException)
        {
        }
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs eventArgs)
    {
        try
        {
            ProcessEvent(eventArgs);
        }
        catch (Exception exception)
        {
            AppLog.Error("HONOR WMI event processing failed", exception);
        }
    }

    private void ProcessEvent(EventArrivedEventArgs eventArgs)
    {
        var value = eventArgs.NewEvent.Properties["Force"]?.Value;
        if (value is null)
            return;

        var code = Convert.ToUInt32(value) & 0xFFFF;
        if (code == MicMuteKeyCode)
        {
            if (Accept(ref _lastMicMuteKeyTime))
                _onMicMuteKey();
            return;
        }

        if (code == CameraKeyCode)
        {
            if (Accept(ref _lastCameraKeyTime))
                _onCameraKey();
            return;
        }

        if (code is 0x2B1 or 0x2B2 or 0x2B3)
        {
            // Right after wake the firmware re-initializes the backlight and emits
            // a spurious level event; ignoring it prevents a false manual override.
            if (_shouldIgnoreBacklightEvent?.Invoke() == true)
                return;

            var level = code switch
            {
                0x2B1 => KeyboardBacklightLevel.Off,
                0x2B2 => KeyboardBacklightLevel.Low,
                _ => KeyboardBacklightLevel.High
            };
            HardwareSettings.KeyboardBacklight = level;
            _onBacklightChanged(level);
            return;
        }

        if (code is not (0x2A0 or 0x2A1 or 0x2A6))
            return;

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastEventTime) < 500)
            return;

        Interlocked.Exchange(ref _lastEventTime, now);
        _currentState = code switch
        {
            0x2A0 => false,
            0x2A1 => true,
            _ => !_currentState
        };
        HardwareSettings.PerformanceModeActive = _currentState;
        _onModeChanged(_currentState);
    }

    /// <summary>
    /// Пропускает первое событие пачки и гасит остальные. Отсчёт сдвигается
    /// и погашенным событием: пока прошивка повторяет нажатие, порог не истекает.
    /// </summary>
    private static bool Accept(ref long lastTime)
    {
        var now = Environment.TickCount64;
        var previous = Interlocked.Exchange(ref lastTime, now);
        return now - previous >= HotkeyDebounceMilliseconds;
    }
}
