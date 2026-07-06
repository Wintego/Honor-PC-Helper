namespace HonorPCHelper;

internal sealed class TouchpadBrightnessService : IDisposable
{
    private const byte GestureIdentifier = 0x0E;
    private static readonly (ushort Vendor, ushort Product)[] SupportedDevices =
    [
        (0x27C6, 0x0F9A),
        (0x35CC, 0x0104)
    ];

    private readonly Action<string> _reportError;
    private readonly Lock _actionLock = new();
    private readonly Lock _lifecycleLock = new();
    private CancellationTokenSource _cancellation = new();
    private (byte Type, byte Direction, long Time) _lastGesture;
    private bool _disposed;

    internal TouchpadBrightnessService(Action<string> reportError)
    {
        _reportError = reportError;
    }

    // Проверяет по интерфейсному пути устройства (WM_DEVICECHANGE), относится ли оно
    // к поддерживаемым тачпадам, чтобы не перезапускать читателей на чужие устройства.
    internal static bool IsSupportedDevicePath(string devicePath)
        => SupportedDevices.Any(device =>
            devicePath.Contains($"vid_{device.Vendor:x4}", StringComparison.OrdinalIgnoreCase));

    internal void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;
            if (!StartCore(_cancellation.Token))
                _reportError(L.T("Совместимый тачпад Honor не найден.",
                    "No compatible Honor touchpad was found."));
        }
    }

    // Пересоздаёт читателей после подключения/отключения устройства
    // (сон, переустановка драйвера тачпада): старые хендлы к этому моменту мертвы.
    internal void Restart()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
            if (StartCore(_cancellation.Token))
                AppLog.Info("Touchpad readers restarted after device change");
        }
    }

    private bool StartCore(CancellationToken cancellation)
    {
        try
        {
            var candidates = HidDevice.Enumerate()
                .Where(device => SupportedDevices.Contains((device.VendorId, device.ProductId)))
                .Where(device => device.UsagePage >= 0xFF00)
                .ToArray();

            foreach (var device in candidates)
                _ = ReadDevice(device, cancellation);

            return candidates.Length > 0;
        }
        catch (Exception exception)
        {
            _reportError($"Ошибка запуска тачпада: {exception.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }

    private async Task ReadDevice(HidDeviceInfo device, CancellationToken cancellation)
    {
        using var handle = device.OpenForReading();
        if (handle.IsInvalid)
            return;

        try
        {
            await using var stream = new FileStream(handle, FileAccess.Read,
                Math.Max(device.InputReportLength, (ushort)64), true);
            var buffer = new byte[Math.Max(device.InputReportLength, (ushort)64)];
            while (!cancellation.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(buffer, cancellation);
                if (count >= 3 && buffer[0] == GestureIdentifier)
                    ProcessGesture(buffer[1], buffer[2]);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Устройство пропало (сон, переустановка драйвера) - читатель завершается,
            // WM_DEVICECHANGE перезапустит его при появлении устройства.
            AppLog.Error($"Touchpad reader stopped: {exception.Message}");
        }
    }

    private void ProcessGesture(byte type, byte direction)
    {
        if (type != 0x03 || direction is not (0x01 or 0x02))
            return;

        lock (_actionLock)
        {
            var now = Environment.TickCount64;
            if (_lastGesture.Type == type && _lastGesture.Direction == direction && now - _lastGesture.Time < 150)
                return;
            _lastGesture = (type, direction, now);

            try
            {
                var up = direction == 0x01;
                // Сначала пробуем виртуальный HID-драйвер (нативный OSD Windows).
                // Если драйвера нет - откат на WMI (без OSD).
                if (!BrightnessVHid.TrySend(up))
                {
                    var step = AppConfig.Current.BrightnessStepPercent;
                    BrightnessController.Change(up ? step : -step);
                }
            }
            catch (Exception exception)
            {
                _reportError($"Не удалось изменить яркость: {exception.Message}");
            }
        }
    }
}
