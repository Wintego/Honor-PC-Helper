namespace HonorPCHelper;

internal sealed class TouchpadBrightnessService : IDisposable
{
    private const byte DirectionUp = 0x01;
    private const byte DirectionDown = 0x02;
    private const int RepeatSuppressMilliseconds = 150;
    // Шаг запасного пути WmiSetBrightness: мельче прошивочных 10%,
    // чтобы движение по краю тачпада меняло яркость плавно.
    private const int BrightnessStepPercent = 3;

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
        => TouchpadVendorLink.SupportedDevices.Any(device =>
            devicePath.Contains($"vid_{device.Vendor:x4}", StringComparison.OrdinalIgnoreCase));

    internal void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;
            if (!StartCore(_cancellation.Token))
                _reportError(L.T("Совместимый тачпад Honor не найден.",
                    "No compatible Honor touchpad was found.",
                    "未找到兼容的荣耀触控板。"));
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
                .Where(device => TouchpadVendorLink.SupportedDevices.Contains((device.VendorId, device.ProductId)))
                .Where(device => device.UsagePage >= 0xFF00)
                .ToArray();

            foreach (var device in candidates)
                _ = ReadDevice(device, cancellation);

            return candidates.Length > 0;
        }
        catch (Exception exception)
        {
            _reportError(L.T(
                $"Ошибка запуска тачпада: {exception.Message}",
                $"Touchpad startup failed: {exception.Message}",
                $"触控板启动失败：{exception.Message}"));
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
                if (count >= 3 && buffer[0] == TouchpadVendorLink.ReportId)
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
        if (type != TouchpadVendorLink.GestureEventCommand
            || direction is not (DirectionUp or DirectionDown))
            return;

        lock (_actionLock)
        {
            var now = Environment.TickCount64;
            if (_lastGesture.Type == type && _lastGesture.Direction == direction
                && now - _lastGesture.Time < RepeatSuppressMilliseconds)
                return;
            _lastGesture = (type, direction, now);

            try
            {
                var up = direction == DirectionUp;
                // Порядок как у PC Manager: сначала ACPI-WMI Honor - шаг делает прошивка,
                // Windows сама рисует штатный OSD. Запасной путь - WmiSetBrightness:
                // яркость меняется, но OSD не будет.
                if (!HonorAcpiBrightness.TryStep(up))
                    BrightnessController.Change(up ? BrightnessStepPercent : -BrightnessStepPercent);
            }
            catch (Exception exception)
            {
                _reportError(L.T(
                    $"Не удалось изменить яркость: {exception.Message}",
                    $"Could not change brightness: {exception.Message}",
                    $"无法调整亮度：{exception.Message}"));
            }
        }
    }
}
