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
    int? MemoryTemperature)
{
    internal bool IsFresh => DateTime.UtcNow - SampledAt < TimeSpan.FromSeconds(30);

    internal string Serialize(string requestId)
        => string.Join('|', requestId, SampledAt.Ticks, Fan1Rpm, Fan2Rpm, CpuTemperature,
            BatteryTemperature, KeyboardBacklightMode, ChargeStart, ChargeEnd, MemoryTemperature);

    internal static bool TryParse(string? value, out HardwareSensorSnapshot snapshot)
    {
        snapshot = default;
        var parts = value?.Split('|');
        // 9 fields is a snapshot written before the memory temperature existed;
        // accept it with that sensor left unknown rather than dropping the whole row.
        if (parts is not { Length: 9 or 10 } || !long.TryParse(parts[1], out var ticks))
            return false;

        snapshot = new HardwareSensorSnapshot(
            new DateTime(ticks, DateTimeKind.Utc),
            ParseNullable(parts[2]), ParseNullable(parts[3]), ParseNullable(parts[4]),
            ParseNullable(parts[5]), ParseNullable(parts[6]), ParseNullable(parts[7]),
            ParseNullable(parts[8]),
            parts.Length > 9 ? ParseNullable(parts[9]) : null);
        return true;
    }

    internal static int? ParseNullable(string value)
        => int.TryParse(value, out var result) ? result : null;
}

/// <summary>
/// Обороты последних измерений: на строку - время в тиках UTC и оба вентилятора.
/// Нужны именно ряды, а не одно значение: см. комментарий в
/// <see cref="HardwareSensorController"/>.
/// </summary>
internal readonly record struct FanSpeedSample(DateTime SampledAt, int? Fan1Rpm, int? Fan2Rpm)
{
    internal string Serialize()
        => string.Join(',', SampledAt.Ticks, Fan1Rpm, Fan2Rpm);

    internal static List<FanSpeedSample> Parse(string? value)
    {
        var samples = new List<FanSpeedSample>();
        foreach (var entry in value?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            var parts = entry.Split(',');
            if (parts.Length != 3 || !long.TryParse(parts[0], out var ticks))
                continue;

            samples.Add(new FanSpeedSample(new DateTime(ticks, DateTimeKind.Utc),
                HardwareSensorSnapshot.ParseNullable(parts[1]),
                HardwareSensorSnapshot.ParseNullable(parts[2])));
        }

        return samples;
    }
}

internal static class HardwareSensorController
{
    private const ulong FanSpeedGetCommand = 0x00000802;
    private const ulong TemperatureGetCommand = 0x00000202;
    private const ulong KeyboardBacklightModeGetCommand = 0x00001306;
    private const ulong BatteryThresholdsGetCommand = 0x00001103;

    // Sensor zones as they are spelled in the firmware: decompiling the ACPI tables
    // shows OemWMIfun's input byte 0 is a "main function" and byte 1 a "sub
    // function" (0x0802 = function 2/8 = AML method GFNS, 0x0202 = GTMP), and GTMP
    // switches over its third byte with a fixed table: 0x00 CDTS (CPU package),
    // 0x0B DDRS (memory), 0x0E BTND (battery), 0x0F ABNT, 0x21..0x28 skin/keyboard/
    // palm-rest temperatures. Everything not in that table returns status 1, which
    // is the AML Default branch - the EC never saw those reads at all.
    private const byte CpuTemperatureZone = 0x00;
    private const byte MemoryTemperatureZone = 0x0B;
    private const byte BatteryTemperatureZone = 0x0E;

    // The EC latches fan speed about once a second: two reads inside that window
    // return identical bytes, and a read landing on an updating latch returns a
    // torn value. Measured on a FMB-P (Core Ultra 5 225H) while a fan held a steady
    // ~3000 RPM: single reads of 0, 6726, 12269 and 26785 RPM within seconds of
    // each other, while the second slot varied by less than 1%.
    //
    // One refresh cannot fix that on its own - a corrupt latch value survives the
    // whole window it belongs to, so back-to-back reads inside a single refresh
    // happily repeat the same impossible number. What does work is a median over
    // several refreshes: each round reads one value per fan, rounds are spaced far
    // enough apart to cross a latch boundary, and the values are kept in the
    // registry so the next hover adds to the same window.
    private const int FanRoundCount = 2;
    private const int FanRoundSpacingMilliseconds = 1100;
    // A laptop fan in this class tops out well below 7000 RPM. Measured on a FMB-P
    // while it audibly ramped up (946 samples): the highest speed the EC ever
    // reported was 3303, while torn latch reads produced 5560, 7547, 14000, 26785
    // and 58252 within the same seconds. 10000 let the first two through, so the
    // ceiling sits at the physical envelope instead of "clearly absurd".
    private const int FanPlausibleMaxRpm = 6500;
    // A tear produces a number nothing else agrees with, so a value only counts
    // when another sample in the window lies near it. The slack scales with the
    // value: a real fan holding 2200 wobbles by tens of RPM between refreshes,
    // while 10% of it is still far below the gap a tear leaves.
    private const int FanCorroborationFloorRpm = 200;
    private const double FanCorroborationRelative = 0.10;
    // Five values rather than three: one corrupt latch window usually lands two
    // samples in a row (both rounds of a refresh), so a median needs at least
    // three others to outvote it. The cost is that a real fan ramp is followed a
    // refresh or two late - acceptable for a hover tooltip, and measured below.
    private const int FanHistoryMaxSamples = 5;
    private static readonly TimeSpan FanHistoryWindow = TimeSpan.FromSeconds(12);

    internal static void ReadAndStore(string requestId)
    {
        using var session = new HonorWmiSession();
        var backlightMode = ReadValue(session, KeyboardBacklightModeGetCommand, 1, "keyboard backlight mode");
        var (chargeStart, chargeEnd) = ReadBatteryThresholds(session);
        // Two rounds one latch window apart: a corrupt value then has to outvote
        // the recent history as well as the other round of this refresh.
        var fans = ReadAndSmoothFans(session);
        var snapshot = new HardwareSensorSnapshot(
            DateTime.UtcNow, fans[0], fans[1],
            ReadTemperature(session, CpuTemperatureZone), ReadTemperature(session, BatteryTemperatureZone),
            backlightMode, chargeStart, chargeEnd,
            ReadTemperature(session, MemoryTemperatureZone));
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

    /// <summary>
    /// Reads both fans and returns the values the tooltip shows: the median of
    /// this refresh together with the recent history, which the refresh itself is
    /// appended to.
    /// </summary>
    private static int?[] ReadAndSmoothFans(HonorWmiSession session)
    {
        var history = FanSpeedSample.Parse(HardwareSettings.FanRpmHistory);
        history.AddRange(ReadFanSamples(session));
        history = RetainRecent(history);
        HardwareSettings.FanRpmHistory = string.Join(';', history.Select(sample => sample.Serialize()));

        return [Median(history, sample => sample.Fan1Rpm), Median(history, sample => sample.Fan2Rpm)];
    }

    private static List<FanSpeedSample> ReadFanSamples(HonorWmiSession session)
    {
        var samples = new List<FanSpeedSample>(FanRoundCount);
        Exception? lastError = null;

        for (var round = 0; round < FanRoundCount; round++)
        {
            if (round > 0)
                Thread.Sleep(FanRoundSpacingMilliseconds);

            int?[] fans = [null, null];
            for (var index = 0; index < fans.Length; index++)
            {
                try
                {
                    var output = session.Call(FanSpeedGetCommand | ((ulong)index << 16));
                    if (output.Length >= 3)
                    {
                        var rpm = output[1] | (output[2] << 8);
                        fans[index] = rpm <= FanPlausibleMaxRpm ? rpm : null;
                    }
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
            }

            if (fans[0].HasValue || fans[1].HasValue)
                samples.Add(new FanSpeedSample(DateTime.UtcNow, fans[0], fans[1]));
        }

        if (lastError is not null)
            AppLog.Error("Could not read fan speed", lastError);

        return samples;
    }

    private static List<FanSpeedSample> RetainRecent(List<FanSpeedSample> history)
    {
        var cutoff = DateTime.UtcNow - FanHistoryWindow;
        var recent = history.Where(sample => sample.SampledAt >= cutoff).ToList();
        return recent.Count > FanHistoryMaxSamples
            ? recent.GetRange(recent.Count - FanHistoryMaxSamples, FanHistoryMaxSamples)
            : recent;
    }

    // A stopped fan reads a steady 0, so the median reports 0 only while most of
    // the window agrees; a lone failed latch update among spinning samples is
    // outvoted instead of flashing as a stalled fan.
    private static int? Median(List<FanSpeedSample> history, Func<FanSpeedSample, int?> selector)
        => CorroboratedMedian(history.Select(selector)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList());

    // Two passes: drop the values no second sample supports, then take the median
    // of what is left. Filtering first matters because a single tear in a five
    // sample window is the middle element - it would otherwise be reported as the
    // fan speed rather than discarded. When nothing corroborates - which happens
    // when every sample in the window is a different speed, i.e. a fan stepping
    // through its range - the result is unknown rather than a guess: the tooltip
    // shows "?" for one refresh instead of a number that is certainly wrong.
    private static int? CorroboratedMedian(List<int> values)
    {
        if (values.Count == 0)
            return null;

        values.Sort();
        var supported = new List<int>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var slack = Math.Max(FanCorroborationFloorRpm, (int)(values[i] * FanCorroborationRelative));
            for (var j = 0; j < values.Count; j++)
            {
                if (j == i || Math.Abs(values[j] - values[i]) > slack)
                    continue;

                supported.Add(values[i]);
                break;
            }
        }

        if (supported.Count == 0)
            return null;

        return supported[supported.Count / 2];
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
