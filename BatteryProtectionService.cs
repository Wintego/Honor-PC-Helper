namespace HonorPCHelper;

/// <summary>
/// Пределы заряда живут в контроллере батареи, и после пробуждения - в первую
/// очередь после закрытия и открытия крышки - прошивка на части моделей
/// возвращает их к 0-100 %. Служба сверяет пороги с режимом, выбранным
/// в меню, и восстанавливает их, если EC их потерял.
///
/// Проверка идёт несколько раз: пороги сбрасываются не в момент пробуждения,
/// а через несколько секунд после него, когда EC заканчивает инициализацию.
/// </summary>
internal sealed class BatteryProtectionService : IDisposable
{
    private const int CheckDelayMilliseconds = 3000;
    private const int CheckCount = 3;
    private readonly SemaphoreSlim _restoreLock = new(1, 1);
    private bool _disposed;

    internal async Task RestoreAfterResumeAsync()
    {
        if (_disposed || HardwareSettings.PreferredBatteryProtection is not { } preferred)
            return;

        // Восстановление идёт само по себе, без участия человека, поэтому оно
        // возможно только через фоновую задачу: показывать запрос UAC после
        // каждого открытия крышки нельзя.
        if (!PrivilegedHardware.AreTasksAvailable() || !await _restoreLock.WaitAsync(0))
            return;

        try
        {
            var expected = BatteryProtectionController.Thresholds(preferred);
            for (var attempt = 0; attempt < CheckCount && !_disposed; attempt++)
            {
                await Task.Delay(CheckDelayMilliseconds);
                if (_disposed)
                    return;

                try
                {
                    if (await ReadThresholdsAsync() == expected)
                        continue;

                    AppLog.Info($"Charge thresholds were lost after resume, restoring {preferred}");
                    if (await PrivilegedHardware.TryRunBatteryTaskSilentlyAsync(preferred))
                        HardwareSettings.BatteryProtection = preferred;
                }
                catch (Exception exception)
                {
                    AppLog.Error("Charge limit restore after resume failed", exception);
                }
            }
        }
        finally
        {
            _restoreLock.Release();
        }
    }

    /// <summary>
    /// Пороги из EC; (null, null), если опрос не удался - тогда настройку
    /// имеет смысл применить заново, а не считать её уцелевшей.
    /// </summary>
    private static async Task<(int? Start, int? End)> ReadThresholdsAsync()
    {
        if (!await PrivilegedHardware.TryReadSensorsSilentlyAsync()
            || !HardwareSensorSnapshot.TryParse(HardwareSettings.SensorSnapshot, out var snapshot))
            return (null, null);

        return (snapshot.ChargeStart, snapshot.ChargeEnd);
    }

    public void Dispose()
    {
        _disposed = true;
        _restoreLock.Dispose();
    }
}
