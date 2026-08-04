using Microsoft.Win32;

namespace HonorPCHelper;

/// <summary>
/// Состояние приложения в HKCU\Software\HonorPCHelper.
///
/// Ветка открывается один раз и остаётся открытой на всё время работы процесса:
/// сборка меню трея читает два десятка значений подряд, а открытие ключа
/// заметно дороже самого чтения. GetValue и SetValue каждый раз обращаются
/// к реестру, поэтому значения, записанные привилегированным экземпляром
/// приложения, видны сразу - на этом построен обмен через PendingHardwareCommand.
/// </summary>
internal static class HardwareSettings
{
    private const string RegistryPath = @"Software\HonorPCHelper";
    private const string KeyboardBacklightValue = "KeyboardBacklightLevel";
    private const string KeyboardBacklightTimeoutValue = "KeyboardBacklightTimeout";
    private const ushort DefaultBacklightTimeout = 60;
    private const string PerformanceModeValue = "PerformanceModeActive";
    private const string BatteryProtectionValue = "BatteryProtectionMode";
    private const string PowerUnlockValue = "PowerUnlockEnabled";
    private const string BacklightScheduleEnabledValue = "BacklightScheduleEnabled";
    private const string BacklightOnHourValue = "BacklightOnHour";
    private const string BacklightOffHourValue = "BacklightOffHour";
    private const string BacklightScheduleLevelValue = "BacklightScheduleLevel";
    private const string TouchpadHapticsValue = "TouchpadHapticsLevel";
    private const string TouchpadEdgeGesturePrefix = "TouchpadEdgeGesture.";
    private const string PendingHardwareCommandValue = "PendingHardwareCommand";
    private const string SensorSnapshotValue = "SensorSnapshot";
    private const string HotkeyValuePrefix = "Hotkey.";

    // Обращения идут из UI-потока, фоновых задач и обработчика событий WMI,
    // поэтому доступ к общему ключу сериализуется.
    private static readonly Lock Gate = new();
    private static RegistryKey? _key;

    // Вызывается только под Gate.
    private static RegistryKey Key() => _key ??= Registry.CurrentUser.CreateSubKey(RegistryPath, true);

    internal readonly record struct TooltipState(
        string? SensorSnapshot,
        bool PerformanceModeActive,
        KeyboardBacklightLevel? KeyboardBacklight,
        BatteryProtectionMode? BatteryProtection);

    /// <summary>Читает всё, что нужно подсказке трея, за один заход.</summary>
    internal static TooltipState ReadTooltipState()
    {
        lock (Gate)
        {
            var key = Key();
            return new TooltipState(
                key.GetValue(SensorSnapshotValue) as string,
                key.GetValue(PerformanceModeValue) as int? is { } performance && performance != 0,
                ParseEnum<KeyboardBacklightLevel>(key.GetValue(KeyboardBacklightValue) as string),
                ParseEnum<BatteryProtectionMode>(key.GetValue(BatteryProtectionValue) as string));
        }
    }

    internal static string? SensorSnapshot
    {
        get => ReadString(SensorSnapshotValue);
        set => WriteString(SensorSnapshotValue, value);
    }

    internal static string? PendingHardwareCommand
    {
        get => ReadString(PendingHardwareCommandValue);
        set => WriteString(PendingHardwareCommandValue, value);
    }

    internal static bool BacklightScheduleEnabled
    {
        get => ReadInt(BacklightScheduleEnabledValue) is { } value && value != 0;
        set => WriteInt(BacklightScheduleEnabledValue, value ? 1 : 0);
    }

    internal static int BacklightOnHour
    {
        get => ReadInt(BacklightOnHourValue) ?? 18;
        set => WriteInt(BacklightOnHourValue, Math.Clamp(value, 0, 23));
    }

    internal static int BacklightOffHour
    {
        get => ReadInt(BacklightOffHourValue) ?? 6;
        set => WriteInt(BacklightOffHourValue, Math.Clamp(value, 0, 23));
    }

    /// <summary>Уровень, который включает расписание. Off здесь бессмысленно, поэтому заменяется на Low.</summary>
    internal static KeyboardBacklightLevel BacklightScheduleLevel
    {
        get => ReadEnum<KeyboardBacklightLevel>(BacklightScheduleLevelValue) is { } level
            && level != KeyboardBacklightLevel.Off
            ? level
            : KeyboardBacklightLevel.Low;
        set => WriteEnum<KeyboardBacklightLevel>(BacklightScheduleLevelValue,
            value == KeyboardBacklightLevel.Off ? KeyboardBacklightLevel.Low : value);
    }

    internal static BatteryProtectionMode? BatteryProtection
    {
        get => ReadEnum<BatteryProtectionMode>(BatteryProtectionValue);
        set => WriteEnum(BatteryProtectionValue, value);
    }

    internal static bool? PowerUnlock
    {
        get => ReadInt(PowerUnlockValue) is { } value ? value != 0 : null;
        set => WriteInt(PowerUnlockValue, value.HasValue ? value.Value ? 1 : 0 : null);
    }

    internal static KeyboardBacklightLevel? KeyboardBacklight
    {
        get => ReadEnum<KeyboardBacklightLevel>(KeyboardBacklightValue);
        set => WriteEnum(KeyboardBacklightValue, value);
    }

    /// <summary>
    /// Таймаут автоотключения подсветки в секундах; 0 - не выключать.
    /// Если пользователь ничего не выбирал, действует умолчание - 1 минута.
    /// </summary>
    internal static ushort KeyboardBacklightTimeout
    {
        get => ReadInt(KeyboardBacklightTimeoutValue) is { } value and >= 0 and <= ushort.MaxValue
            ? (ushort)value
            : DefaultBacklightTimeout;
        set => WriteInt(KeyboardBacklightTimeoutValue, value);
    }

    internal static TouchpadHapticsLevel? TouchpadHaptics
    {
        get => ReadEnum<TouchpadHapticsLevel>(TouchpadHapticsValue);
        set => WriteEnum(TouchpadHapticsValue, value);
    }

    internal static bool PerformanceModeActive
    {
        get => ReadInt(PerformanceModeValue) is { } value && value != 0;
        set => WriteInt(PerformanceModeValue, value ? 1 : 0);
    }

    /// <summary>
    /// Состояние жеста на краю экрана. null - пользователь его не менял,
    /// действует умолчание прошивки (жест включён).
    /// </summary>
    internal static bool? GetTouchpadEdgeGesture(TouchpadEdgeGesture gesture)
        => ReadInt(TouchpadEdgeGesturePrefix + gesture) is { } value ? value != 0 : null;

    internal static void SetTouchpadEdgeGesture(TouchpadEdgeGesture gesture, bool? enabled)
        => WriteInt(TouchpadEdgeGesturePrefix + gesture, enabled.HasValue ? enabled.Value ? 1 : 0 : null);

    /// <summary>
    /// Пользовательское сочетание клавиш для действия. null - значение не задавалось,
    /// действует умолчание; "None" - пользователь отключил сочетание.
    /// </summary>
    internal static string? GetHotkey(string action) => ReadString(HotkeyValuePrefix + action);

    internal static void SetHotkey(string action, string? value)
        => WriteString(HotkeyValuePrefix + action, value);

    private static string? ReadString(string name)
    {
        lock (Gate)
            return Key().GetValue(name) as string;
    }

    private static void WriteString(string name, string? value)
    {
        lock (Gate)
        {
            if (value is null)
                Key().DeleteValue(name, false);
            else
                Key().SetValue(name, value, RegistryValueKind.String);
        }
    }

    private static int? ReadInt(string name)
    {
        lock (Gate)
            return Key().GetValue(name) as int?;
    }

    private static void WriteInt(string name, int? value)
    {
        lock (Gate)
        {
            if (value is null)
                Key().DeleteValue(name, false);
            else
                Key().SetValue(name, value.Value, RegistryValueKind.DWord);
        }
    }

    private static T? ReadEnum<T>(string name) where T : struct, Enum
        => ParseEnum<T>(ReadString(name));

    private static void WriteEnum<T>(string name, T? value) where T : struct, Enum
        => WriteString(name, value?.ToString());

    private static T? ParseEnum<T>(string? value) where T : struct, Enum
        => Enum.TryParse<T>(value, out var parsed) ? parsed : null;
}
