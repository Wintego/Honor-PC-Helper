namespace HonorPCHelper;

internal sealed class BacklightScheduleService : IDisposable
{
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly System.Threading.Timer _timer;
    private DateTime? _manualOverrideUntil;
    private bool _disposed;

    internal BacklightScheduleService()
    {
        _timer = new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    internal void Start() => _ = ApplyIfNeededAsync();

    internal async Task RestoreAfterResumeAsync()
    {
        if (_disposed)
            return;

        // Resume events from the firmware must not count as a manual override.
        _manualOverrideUntil = null;
        var level = DesiredLevel(DateTime.Now);

        // The firmware may reset the backlight a few times while the machine
        // finishes waking, so re-apply the target level several times.
        for (var attempt = 0; attempt < 3 && !_disposed; attempt++)
        {
            await Task.Delay(2000);
            if (_disposed)
                return;

            try
            {
                await PrivilegedHardware.TryRunBacklightTaskAsync(level);
                await PrivilegedHardware.TryRunBacklightTimeoutTaskAsync(
                    HardwareSettings.KeyboardBacklightTimeout);
            }
            catch (Exception exception)
            {
                AppLog.Error("Backlight restore after resume failed", exception);
            }
        }

        HardwareSettings.KeyboardBacklight = level;
        _manualOverrideUntil = null;
        await ApplyIfNeededAsync(force: true);
    }

    private static KeyboardBacklightLevel DesiredLevel(DateTime now)
    {
        if (HardwareSettings.BacklightScheduleEnabled)
            return ShouldBeOn(now)
                ? HardwareSettings.BacklightScheduleLevel
                : KeyboardBacklightLevel.Off;

        return HardwareSettings.KeyboardBacklight ?? KeyboardBacklightLevel.Off;
    }

    internal async Task SettingsChangedAsync()
    {
        _manualOverrideUntil = null;
        await ApplyIfNeededAsync(force: true);
    }

    internal void SetManualOverride()
    {
        if (!HardwareSettings.BacklightScheduleEnabled)
            return;

        _manualOverrideUntil = GetNextBoundary(DateTime.Now);
        ScheduleNextCheck(DateTime.Now);
    }

    internal async Task ApplyIfNeededAsync(bool force = false)
    {
        if (_disposed || !await _applyLock.WaitAsync(0))
            return;

        try
        {
            if (!HardwareSettings.BacklightScheduleEnabled)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            var now = DateTime.Now;
            if (_manualOverrideUntil.HasValue && now < _manualOverrideUntil.Value)
            {
                ScheduleNextCheck(now);
                return;
            }

            _manualOverrideUntil = null;
            var level = ShouldBeOn(now)
                ? HardwareSettings.BacklightScheduleLevel
                : KeyboardBacklightLevel.Off;

            try
            {
                if ((force || HardwareSettings.KeyboardBacklight != level)
                    && await PrivilegedHardware.TryRunBacklightTaskAsync(level))
                {
                    HardwareSettings.KeyboardBacklight = level;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Backlight schedule hardware command failed", exception);
            }

            ScheduleNextCheck(now);
        }
        finally
        {
            _applyLock.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    private async void OnTimer(object? state)
    {
        try
        {
            await ApplyIfNeededAsync(force: true);
        }
        catch (Exception exception)
        {
            AppLog.Error("Backlight schedule failed", exception);
        }
        finally
        {
            if (!_disposed && HardwareSettings.BacklightScheduleEnabled)
                ScheduleNextCheck(DateTime.Now);
        }
    }

    private void ScheduleNextCheck(DateTime now)
    {
        if (_disposed || !HardwareSettings.BacklightScheduleEnabled)
            return;

        var next = _manualOverrideUntil.HasValue && _manualOverrideUntil.Value > now
            ? _manualOverrideUntil.Value
            : GetNextBoundary(now);
        var due = next - now;
        _timer.Change(due < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : due, Timeout.InfiniteTimeSpan);
    }

    private static bool ShouldBeOn(DateTime now)
    {
        var onHour = HardwareSettings.BacklightOnHour;
        var offHour = HardwareSettings.BacklightOffHour;
        return onHour < offHour
            ? now.Hour >= onHour && now.Hour < offHour
            : now.Hour >= onHour || now.Hour < offHour;
    }

    private static DateTime GetNextBoundary(DateTime now)
    {
        var on = now.Date.AddHours(HardwareSettings.BacklightOnHour);
        var off = now.Date.AddHours(HardwareSettings.BacklightOffHour);
        if (on <= now)
            on = on.AddDays(1);
        if (off <= now)
            off = off.AddDays(1);
        return on < off ? on : off;
    }
}
