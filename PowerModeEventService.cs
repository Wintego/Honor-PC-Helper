using System.Management;

namespace HonorPCHelper;

internal sealed class PowerModeEventService : IDisposable
{
    private readonly Action<bool> _onModeChanged;
    private readonly Action<KeyboardBacklightLevel> _onBacklightChanged;
    private readonly Func<bool>? _shouldIgnoreBacklightEvent;
    private ManagementEventWatcher? _watcher;
    private long _lastEventTime;
    private volatile bool _currentState = HardwareSettings.PerformanceModeActive;

    internal PowerModeEventService(
        Action<bool> onModeChanged,
        Action<KeyboardBacklightLevel> onBacklightChanged,
        Func<bool>? shouldIgnoreBacklightEvent = null)
    {
        _onModeChanged = onModeChanged;
        _onBacklightChanged = onBacklightChanged;
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
}
