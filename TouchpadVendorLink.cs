using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HonorPCHelper;

/// <summary>
/// Канал управления прошивкой тачпада Honor (Goodix force pad).
///
/// Все настройки уходят обычным output report длиной 9 байт на vendor-коллекцию
/// COL07 (UsagePage 0xFF00, Usage 0x01, In=9 / Out=9):
///
///     0E &lt;подкоманда&gt; &lt;значение&gt; 00 00 00 00 00 00
///
/// где 0x0E - Report ID. Известные подкоманды сняты перехватом WriteFile
/// в MBAMessageCenter.exe (хост MagicTouchPadPlugin.dll) при переключении
/// настроек в MagicTouchPadSettingUI.exe:
///
/// | 0x02 | интенсивность вибрации         | 0 низкая / 1 умеренная / 2 высокая |
/// | 0x03 | событие жеста края (input)     | 1 / 2 - направление                |
/// | 0x07 | жест яркости, левый край       | 0 / 1                              |
/// | 0x08 | жест громкости, правый край    | 0 / 1                              |
/// | 0x09 | Центр управления, правый край  | 0 / 1                              |
/// | 0x0A | закрыть/свернуть окно          | 0 / 1                              |
///
/// PC Manager дублирует состояние в HKCU\SOFTWARE\PCManager\TouchPadSetting,
/// но реестр там - только хранилище: реально настройку применяет этот репорт.
/// </summary>
internal static class TouchpadVendorLink
{
    internal const byte ReportId = 0x0E;
    internal const byte HapticsCommand = 0x02;
    /// <summary>Приходит во входящем репорте при свайпе вдоль края, значение - направление.</summary>
    internal const byte GestureEventCommand = 0x03;
    internal const byte EdgeBrightnessCommand = 0x07;
    internal const byte EdgeVolumeCommand = 0x08;

    private const int DefaultReportLength = 9;
    private const ushort VendorUsagePage = 0xFF00;
    private const ushort VendorUsage = 0x0001;

    /// <summary>VID/PID тачпадов Honor, с которыми работает vendor-протокол.</summary>
    internal static readonly (ushort Vendor, ushort Product)[] SupportedDevices =
    [
        (0x27C6, 0x0F9A),
        (0x35CC, 0x0104)
    ];

    internal static bool IsSupported() => FindDevice() is not null;

    /// <summary>
    /// Пишет подкоманду в прошивку. Бросает исключение, если устройство
    /// не найдено или репорт не удалось записать.
    /// </summary>
    internal static void Send(byte command, byte value)
    {
        var device = FindDevice()
            ?? throw new InvalidOperationException(L.T(
                "Совместимый тачпад Honor не найден.",
                "No compatible Honor touchpad was found.",
                "未找到兼容的荣耀触控板。"));

        using var handle = Open(device.Path);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T(
                "Не удалось открыть тачпад для записи.",
                "Could not open the touchpad for writing.",
                "无法打开触控板进行写入。"));

        // Длина буфера должна совпадать с OutputReportByteLength устройства,
        // иначе HID-стек отклоняет запись с ERROR_INVALID_PARAMETER.
        var length = device.OutputReportLength > 0 ? device.OutputReportLength : DefaultReportLength;
        var report = new byte[length];
        report[0] = ReportId;
        report[1] = command;
        report[2] = value;

        if (!NativeMethods.WriteFile(handle, report, (uint)report.Length, out var written, IntPtr.Zero)
            || written != report.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T(
                "Не удалось записать настройку в тачпад.",
                "Could not write the setting to the touchpad.",
                "无法将设置写入触控板。"));
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
            AppLog.Error("Failed to enumerate HID devices for the Honor touchpad", exception);
            return null;
        }
    }
}
