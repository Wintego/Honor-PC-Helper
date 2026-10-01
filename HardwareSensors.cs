namespace HonorPCHelper;

internal readonly record struct HardwareSensorSnapshot(
    DateTime SampledAt,
    int? Fan1Rpm,
    int? Fan2Rpm,
    int? CpuTemperature,
    int? BatteryTemperature,
    int? KeyboardBacklightMode,
    int? ChargeStart,
    int? ChargeEnd,
    int? Fan1TargetRpm,
    int? Fan2TargetRpm,
    int? PerformanceMode)
{
    internal bool IsFresh => DateTime.UtcNow - SampledAt < TimeSpan.FromSeconds(30);

    internal string Serialize(string requestId)
        => string.Join('|', requestId, SampledAt.Ticks, Fan1Rpm, Fan2Rpm, CpuTemperature,
            BatteryTemperature, KeyboardBacklightMode, ChargeStart, ChargeEnd,
            Fan1TargetRpm, Fan2TargetRpm, PerformanceMode);

    internal static bool TryParse(string? value, out HardwareSensorSnapshot snapshot)
    {
        snapshot = default;
        var parts = value?.Split('|');
        // Девять полей пишут прежние версии: фоновая задача может ещё указывать на старый файл.
        if (parts is not { Length: 9 or 12 } || !long.TryParse(parts[1], out var ticks))
            return false;

        var extended = parts.Length == 12;
        snapshot = new HardwareSensorSnapshot(
            new DateTime(ticks, DateTimeKind.Utc),
            ParseNullable(parts[2]), ParseNullable(parts[3]), ParseNullable(parts[4]),
            ParseNullable(parts[5]), ParseNullable(parts[6]), ParseNullable(parts[7]),
            ParseNullable(parts[8]),
            extended ? ParseNullable(parts[9]) : null,
            extended ? ParseNullable(parts[10]) : null,
            extended ? ParseNullable(parts[11]) : null);
        return true;
    }

    private static int? ParseNullable(string value)
        => int.TryParse(value, out var result) ? result : null;
}

internal static class HardwareSensorController
{
    private const ulong FanSpeedGetCommand = 0x00000802;
    private const ulong TemperatureGetCommand = 0x00000202;
    private const ulong KeyboardBacklightModeGetCommand = 0x00001306;
    private const ulong BatteryThresholdsGetCommand = 0x00001103;
    private const ulong PerformanceModeGetCommand = 0x00000E04;

    // Значения 04 0E: 0 - умный, 1 - производительный, 3 - HUNTER.
    // HUNTER снят с MagicBook Pro 16 HUNTER 2024 (issue #7).
    private const int SmartMode = 0;
    private const int PerformanceMode = 1;
    private const int HunterMode = 3;

    internal static void ReadAndStore(string requestId)
    {
        using var session = new HonorWmiSession();
        var backlightMode = ReadValue(session, KeyboardBacklightModeGetCommand, 1, "keyboard backlight mode");
        var (chargeStart, chargeEnd) = ReadBatteryThresholds(session);
        var (fan1, fan1Target) = ReadFan(session, 0);
        var (fan2, fan2Target) = ReadFan(session, 1);
        var performanceMode = ReadPerformanceMode(session);
        var snapshot = new HardwareSensorSnapshot(
            DateTime.UtcNow, fan1, fan2,
            ReadTemperature(session, 0x00), ReadTemperature(session, 0x0E),
            backlightMode, chargeStart, chargeEnd,
            fan1Target, fan2Target, performanceMode);
        HardwareSettings.SensorSnapshot = snapshot.Serialize(requestId);

        // Режим мог смениться в обход приложения: из PC Manager или до его запуска.
        // HUNTER приложение не переключает, поэтому производительный режим
        // в нём считается выключенным - иначе уход в сон снял бы HUNTER.
        if (performanceMode is SmartMode or PerformanceMode or HunterMode)
        {
            HardwareSettings.HunterModeActive = performanceMode == HunterMode;
            HardwareSettings.PerformanceModeActive = performanceMode == PerformanceMode;
        }

        HardwareSettings.KeyboardBacklight = backlightMode switch
        {
            0x02 => KeyboardBacklightLevel.Off,
            0x03 => KeyboardBacklightLevel.Low,
            0x04 => KeyboardBacklightLevel.High,
            _ => HardwareSettings.KeyboardBacklight
        };
        HardwareSettings.BatteryProtection =
            BatteryProtectionController.FromThresholds(chargeStart, chargeEnd)
            ?? HardwareSettings.BatteryProtection;
    }

    // Ответ 02 08: байты 1-2 - фактические обороты, 3-4 - целевые, на которые выводит прошивка.
    private static (int? Actual, int? Target) ReadFan(HonorWmiSession session, byte index)
    {
        try
        {
            var output = session.Call(FanSpeedGetCommand | ((ulong)index << 16));
            return (output.Length >= 3 ? output[1] | (output[2] << 8) : null,
                output.Length >= 5 ? output[3] | (output[4] << 8) : null);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not read fan {index + 1} speed", exception);
            return (null, null);
        }
    }

    private static int? ReadPerformanceMode(HonorWmiSession session)
    {
        try
        {
            var output = session.Call(PerformanceModeGetCommand);
            return output.Length > 1 ? output[1] : null;
        }
        catch (HonorWmiCommandException)
        {
            // Модель без этой команды: отказ повторялся бы при каждом опросе датчиков.
            return null;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read performance mode", exception);
            return null;
        }
    }

    private static int? ReadTemperature(HonorWmiSession session, byte zone)
    {
        try
        {
            var output = session.Call(TemperatureGetCommand | ((ulong)zone << 16));
            return output.Length >= 3 ? output[2] : null;
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not read temperature zone 0x{zone:X2}", exception);
            return null;
        }
    }

    // Both charge thresholds come back in a single response, so read them together.
    private static (int? Start, int? End) ReadBatteryThresholds(HonorWmiSession session)
    {
        try
        {
            var output = session.Call(BatteryThresholdsGetCommand);
            var start = output.Length > 1 ? (int?)output[1] : null;
            var end = output.Length > 2 ? (int?)output[2] : null;
            return (start, end);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read battery charge thresholds", exception);
            return (null, null);
        }
    }

    private static int? ReadValue(HonorWmiSession session, ulong command, int offset, string name)
    {
        try
        {
            var output = session.Call(command);
            return output.Length > offset ? output[offset] : null;
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not read {name}", exception);
            return null;
        }
    }
}
