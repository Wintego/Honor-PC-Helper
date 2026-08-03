namespace HonorPCHelper;

internal enum TouchpadHapticsLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// Интенсивность виброотклика тачпада Honor (Goodix force pad).
/// Подкоманда 0x02 vendor-репорта, см. <see cref="TouchpadVendorLink"/>:
/// 0 - низкая, 1 - умеренная, 2 - высокая.
/// </summary>
internal static class TouchpadHapticsController
{
    internal static bool IsSupported() => TouchpadVendorLink.IsSupported();

    /// <summary>
    /// Применяет уровень к прошивке тачпада. Бросает исключение, если устройство
    /// не найдено или репорт не удалось записать.
    /// </summary>
    internal static void SetLevel(TouchpadHapticsLevel level)
        => TouchpadVendorLink.Send(TouchpadVendorLink.HapticsCommand, (byte)level);

    /// <summary>
    /// Повторно применяет сохранённый уровень: прошивка сбрасывает его
    /// при переподключении устройства и после выхода из сна.
    /// </summary>
    internal static void Reapply()
    {
        var level = HardwareSettings.TouchpadHaptics;
        if (level is null)
            return;

        try
        {
            SetLevel(level.Value);
        }
        catch (Exception exception)
        {
            AppLog.Error("Failed to reapply touchpad haptics level", exception);
        }
    }
}
