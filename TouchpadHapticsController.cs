using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HonorPCHelper;

internal enum TouchpadHapticsLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// Интенсивность виброотклика тачпада Honor (Goodix force pad).
///
/// Протокол снят перехватом WriteFile в MBAMessageCenter.exe (хост плагина
/// MagicTouchPadPlugin.dll) при переключении уровня в MagicTouchPadSettingUI.exe:
/// на vendor-коллекцию тачпада (UsagePage 0xFF00, Usage 0x01) уходит output report
/// длиной 9 байт вида
///
///     0E 02 &lt;level&gt; 00 00 00 00 00 00
///
/// где 0x0E - Report ID, 0x02 - подкоманда "интенсивность вибрации",
/// level: 0 - низкая, 1 - умеренная, 2 - высокая.
/// Из этой же коллекции приходят события жеста края (0E 03 &lt;направление&gt;),
/// которые читает TouchpadBrightnessService.
/// </summary>
internal static class TouchpadHapticsController
{
    private const byte ReportId = 0x0E;
    private const byte HapticsCommand = 0x02;
    private const int ReportLength = 9;
    private const ushort VendorUsagePage = 0xFF00;
    private const ushort VendorUsage = 0x0001;

    private static readonly (ushort Vendor, ushort Product)[] SupportedDevices =
    [
        (0x27C6, 0x0F9A),
        (0x35CC, 0x0104)
    ];

    internal static bool IsSupported() => FindDevice() is not null;

    /// <summary>
    /// Применяет уровень к прошивке тачпада. Бросает исключение, если устройство
    /// не найдено или репорт не удалось записать.
    /// </summary>
    internal static void SetLevel(TouchpadHapticsLevel level)
    {
        var device = FindDevice()
            ?? throw new InvalidOperationException(L.T(
                "Совместимый тачпад Honor не найден.",
                "No compatible Honor touchpad was found."));

        using var handle = Open(device.Path);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T(
                "Не удалось открыть тачпад для записи.",
                "Could not open the touchpad for writing."));

        // Длина буфера должна совпадать с OutputReportByteLength устройства,
        // иначе HID-стек отклоняет запись с ERROR_INVALID_PARAMETER.
        var length = device.OutputReportLength > 0 ? device.OutputReportLength : ReportLength;
        var report = new byte[length];
        report[0] = ReportId;
        report[1] = HapticsCommand;
        report[2] = (byte)level;

        if (!NativeMethods.WriteFile(handle, report, (uint)report.Length, out var written, IntPtr.Zero)
            || written != report.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T(
                "Не удалось записать настройку вибрации в тачпад.",
                "Could not write the haptics setting to the touchpad."));
        }
    }

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

    private static SafeFileHandle Open(string path) => NativeMethods.CreateFile(
        path, NativeMethods.GenericWrite,
        NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
        IntPtr.Zero, NativeMethods.OpenExisting, 0, IntPtr.Zero);

    private static HidDeviceInfo? FindDevice()
    {
        try
        {
            return HidDevice.Enumerate().FirstOrDefault(device =>
                SupportedDevices.Contains((device.VendorId, device.ProductId))
                && device.UsagePage == VendorUsagePage
                && device.Usage == VendorUsage
                && device.OutputReportLength >= 3);
        }
        catch (Exception exception)
        {
            AppLog.Error("Failed to enumerate HID devices for touchpad haptics", exception);
            return null;
        }
    }
}
