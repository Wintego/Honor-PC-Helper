namespace HonorPCHelper;

internal readonly record struct HardwareSensorSnapshot(
    DateTime SampledAt,
    int? Fan1Rpm,
    int? Fan2Rpm,
    int? CpuTemperature,
    int? BatteryTemperature,
    int? KeyboardBacklightMode,
    int? ChargeStart,
    int? ChargeEnd)
{
    internal bool IsFresh => DateTime.UtcNow - SampledAt < TimeSpan.FromSeconds(30);

    internal string Serialize(string requestId)
        => string.Join('|', requestId, SampledAt.Ticks, Fan1Rpm, Fan2Rpm, CpuTemperature,
            BatteryTemperature, KeyboardBacklightMode, ChargeStart, ChargeEnd);

    internal static bool TryParse(string? value, out HardwareSensorSnapshot snapshot)
    {
        snapshot = default;
        var parts = value?.Split('|');
        if (parts is not { Length: 9 } || !long.TryParse(parts[1], out var ticks))
            return false;

        snapshot = new HardwareSensorSnapshot(
            new DateTime(ticks, DateTimeKind.Utc),
            ParseNullable(parts[2]), ParseNullable(parts[3]), ParseNullable(parts[4]),
            ParseNullable(parts[5]), ParseNullable(parts[6]), ParseNullable(parts[7]),
            ParseNullable(parts[8]));
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

    internal static void ReadAndStore(string requestId)
    {
        using var session = new HonorWmiSession();
        var backlightMode = ReadValue(session, KeyboardBacklightModeGetCommand, 1, "keyboard backlight mode");
        var (chargeStart, chargeEnd) = ReadBatteryThresholds(session);
        var snapshot = new HardwareSensorSnapshot(
            DateTime.UtcNow, ReadFan(session, 0), ReadFan(session, 1),
            ReadTemperature(session, 0x00), ReadTemperature(session, 0x0E),
            backlightMode, chargeStart, chargeEnd);
        HardwareSettings.SensorSnapshot = snapshot.Serialize(requestId);

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

    private static int? ReadFan(HonorWmiSession session, byte index)
    {
        try
        {
            var output = session.Call(FanSpeedGetCommand | ((ulong)index << 16));
            return output.Length >= 3 ? output[1] | (output[2] << 8) : null;
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not read fan {index + 1} speed", exception);
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
