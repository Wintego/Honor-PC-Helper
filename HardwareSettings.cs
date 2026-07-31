using Microsoft.Win32;

namespace HonorPCHelper;

internal static class HardwareSettings
{
    private const string RegistryPath = @"Software\HonorPCHelper";
    private const string KeyboardBacklightValue = "KeyboardBacklightLevel";
    private const string KeyboardBacklightTimeoutValue = "KeyboardBacklightTimeout";
    private const string PerformanceModeValue = "PerformanceModeActive";
    private const string BatteryProtectionValue = "BatteryProtectionMode";
    private const string PowerUnlockValue = "PowerUnlockEnabled";
    private const string BacklightScheduleEnabledValue = "BacklightScheduleEnabled";
    private const string BacklightOnHourValue = "BacklightOnHour";
    private const string BacklightOffHourValue = "BacklightOffHour";
    private const string BacklightScheduleLevelValue = "BacklightScheduleLevel";
    private const string PendingHardwareCommandValue = "PendingHardwareCommand";
    private const string SensorSnapshotValue = "SensorSnapshot";
    private const string HotkeyValuePrefix = "Hotkey.";

    internal readonly record struct TooltipState(
        string? SensorSnapshot,
        bool PerformanceModeActive,
        KeyboardBacklightLevel? KeyboardBacklight,
        BatteryProtectionMode? BatteryProtection);

    /// <summary>
    /// Reads every value the tray tooltip needs through a single registry open
    /// instead of one open per property.
    /// </summary>
    internal static TooltipState ReadTooltipState()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (key is null)
            return new TooltipState(null, false, null, null);

        var snapshot = key.GetValue(SensorSnapshotValue) as string;
        var performance = key.GetValue(PerformanceModeValue) is int p && p != 0;
        var backlight = Enum.TryParse<KeyboardBacklightLevel>(
            key.GetValue(KeyboardBacklightValue) as string, out var level)
            ? level
            : (KeyboardBacklightLevel?)null;
        var battery = Enum.TryParse<BatteryProtectionMode>(
            key.GetValue(BatteryProtectionValue) as string, out var mode)
            ? mode
            : (BatteryProtectionMode?)null;
        return new TooltipState(snapshot, performance, backlight, battery);
    }

    internal static string? SensorSnapshot
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(SensorSnapshotValue) as string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            if (value is null)
                key.DeleteValue(SensorSnapshotValue, false);
            else
                key.SetValue(SensorSnapshotValue, value, RegistryValueKind.String);
        }
    }

    internal static string? PendingHardwareCommand
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(PendingHardwareCommandValue) as string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            if (value is null)
                key.DeleteValue(PendingHardwareCommandValue, false);
            else
                key.SetValue(PendingHardwareCommandValue, value, RegistryValueKind.String);
        }
    }

    internal static bool BacklightScheduleEnabled
    {
        get => ReadInt(BacklightScheduleEnabledValue, 0) != 0;
        set => WriteInt(BacklightScheduleEnabledValue, value ? 1 : 0);
    }

    internal static int BacklightOnHour
    {
        get => ReadInt(BacklightOnHourValue, 18);
        set => WriteInt(BacklightOnHourValue, Math.Clamp(value, 0, 23));
    }

    internal static int BacklightOffHour
    {
        get => ReadInt(BacklightOffHourValue, 6);
        set => WriteInt(BacklightOffHourValue, Math.Clamp(value, 0, 23));
    }

    internal static KeyboardBacklightLevel BacklightScheduleLevel
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var value = key?.GetValue(BacklightScheduleLevelValue) as string;
            return Enum.TryParse<KeyboardBacklightLevel>(value, out var level) && level != KeyboardBacklightLevel.Off
                ? level
                : KeyboardBacklightLevel.High;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            key.SetValue(BacklightScheduleLevelValue,
                value == KeyboardBacklightLevel.Off ? KeyboardBacklightLevel.High.ToString() : value.ToString());
        }
    }

    internal static BatteryProtectionMode? BatteryProtection
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var value = key?.GetValue(BatteryProtectionValue) as string;
            return Enum.TryParse<BatteryProtectionMode>(value, out var mode) ? mode : null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            if (value.HasValue)
                key.SetValue(BatteryProtectionValue, value.Value.ToString());
            else
                key.DeleteValue(BatteryProtectionValue, false);
        }
    }

    internal static bool? PowerUnlock
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(PowerUnlockValue) is int value ? value != 0 : null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            if (value.HasValue)
                key.SetValue(PowerUnlockValue, value.Value ? 1 : 0, RegistryValueKind.DWord);
            else
                key.DeleteValue(PowerUnlockValue, false);
        }
    }

    internal static KeyboardBacklightLevel? KeyboardBacklight
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var value = key?.GetValue(KeyboardBacklightValue) as string;
            return Enum.TryParse<KeyboardBacklightLevel>(value, out var level) ? level : null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            if (value.HasValue)
                key.SetValue(KeyboardBacklightValue, value.Value.ToString());
            else
                key.DeleteValue(KeyboardBacklightValue, false);
        }
    }

    internal static ushort? KeyboardBacklightTimeout
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(KeyboardBacklightTimeoutValue) is int value
                && value is >= ushort.MinValue and <= ushort.MaxValue
                ? (ushort)value
                : null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            if (value.HasValue)
                key.SetValue(KeyboardBacklightTimeoutValue, (int)value.Value, RegistryValueKind.DWord);
            else
                key.DeleteValue(KeyboardBacklightTimeoutValue, false);
        }
    }

    internal static bool PerformanceModeActive
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(PerformanceModeValue) is int value && value != 0;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            key.SetValue(PerformanceModeValue, value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    /// <summary>
    /// Пользовательское сочетание клавиш для действия. null - значение не задавалось,
    /// действует умолчание; "None" - пользователь отключил сочетание.
    /// </summary>
    internal static string? GetHotkey(string action)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return key?.GetValue(HotkeyValuePrefix + action) as string;
    }

    internal static void SetHotkey(string action, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
        if (value is null)
            key.DeleteValue(HotkeyValuePrefix + action, false);
        else
            key.SetValue(HotkeyValuePrefix + action, value, RegistryValueKind.String);
    }

    private static int ReadInt(string name, int defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return key?.GetValue(name) is int value ? value : defaultValue;
    }

    private static void WriteInt(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }
}
