using System.Management;

namespace HonorPCHelper;

internal static class DiagnosticsService
{
    // NotifyIcon.Text не может быть длиннее 127 символов.
    private const int MaxTooltipLength = 127;

    private const int PowerCacheMilliseconds = 2000;

    /// <summary>Снимок мощности вместе со временем измерения: читается и пишется целиком.</summary>
    private sealed record PowerSample(double? Watts, long Ticks);

    private static PowerSample? _power;

    // Подсказка пересобирается при каждом наведении на иконку, поэтому строки
    // складываются в буфер, а локализуются только литералы: интерполяция
    // обоих языковых вариантов сразу удваивала бы аллокации.
    [ThreadStatic] private static System.Text.StringBuilder? _buffer;

    /// <summary>
    /// Tooltip text for the tray icon. The Russian wording overruns the 127
    /// characters NotifyIcon.Text accepts as soon as the performance mode and the
    /// hardware lines are shown together, and the readings at the end are exactly
    /// what the user is looking for, so an overflowing tooltip is rebuilt once more
    /// with short labels before anything is dropped.
    /// </summary>
    internal static string BuildCompactToolTip()
    {
        var text = BuildTooltip(compact: false);
        if (text.Length <= MaxTooltipLength)
            return text;

        var compact = BuildTooltip(compact: true);
        if (compact.Length <= MaxTooltipLength)
            return compact;

        // Still too long: keep the leading lines that fit whole rather than
        // chopping a reading in half.
        var cut = compact[..MaxTooltipLength];
        var lastLineBreak = cut.LastIndexOfAny(['\r', '\n']);
        return lastLineBreak > 0 ? cut[..lastLineBreak] : cut;
    }

    /// <summary>
    /// Variant of <see cref="L.T"/> for wording that only needs shortening where
    /// it is long - Russian. The English and Chinese texts already fit.
    /// </summary>
    private static string Label(bool compact, string compactRussian, string russian, string english, string? chinese = null)
        => L.T(compact ? compactRussian : russian, english, chinese);

    private static string BuildTooltip(bool compact)
    {
        var state = HardwareSettings.ReadTooltipState();
        var hasHardwareState = HardwareSensorSnapshot.TryParse(
            state.SensorSnapshot, out var hardwareState) && hardwareState.IsFresh;
        var mode = state.PerformanceModeActive
            ? Label(compact, "произв.", "производительный", "performance", "高性能")
            : L.T("умный", "smart", "智能");
        var backlightLevel = hasHardwareState
            ? hardwareState.KeyboardBacklightMode switch
            {
                0x02 => KeyboardBacklightLevel.Off,
                0x03 => KeyboardBacklightLevel.Low,
                0x04 => KeyboardBacklightLevel.High,
                _ => state.KeyboardBacklight
            }
            : state.KeyboardBacklight;
        var backlight = backlightLevel switch
        {
            KeyboardBacklightLevel.Off => L.T("выкл.", "off", "关"),
            KeyboardBacklightLevel.Low => L.T("слабая", "weak", "弱"),
            KeyboardBacklightLevel.High => L.T("сильная", "strong", "强"),
            _ => "?"
        };
        var protection = hasHardwareState && hardwareState.ChargeStart.HasValue && hardwareState.ChargeEnd.HasValue
            ? $"{hardwareState.ChargeStart}–{hardwareState.ChargeEnd}%"
            : state.BatteryProtection switch
            {
                BatteryProtectionMode.Home => "40–70%",
                BatteryProtectionMode.Office => "70–90%",
                BatteryProtectionMode.Travel => "95–100%",
                BatteryProtectionMode.Disabled => L.T("выкл.", "off", "关"),
                _ => "?"
            };
        var text = _buffer ??= new System.Text.StringBuilder(MaxTooltipLength + 32);
        text.Clear();
        text.Append(Label(compact, "Реж.: ", "Режим: ", "Mode: ", "模式：")).Append(mode);
        text.AppendLine().Append(Label(compact, "Свет: ", "Подсветка: ", "Backlight: ", "背光：")).Append(backlight);
        text.AppendLine().Append(Label(compact, "Огр. заряда: ", "Ограничение заряда: ", "Charge limit: ", "充电限制：")).Append(protection);

        var power = ReadBatteryPowerWatts();
        if (power.HasValue)
        {
            // This is the battery's charge/discharge rate, not the machine's
            // power draw: the EC exposes nothing else without administrator
            // rights. Saying so keeps a held battery from looking like a fault.
            text.AppendLine().Append(Label(compact, "Бат.: ", "Батарея: ", "Battery: ", "电池功率："));
            AppendPower(text, power.Value);
        }

        if (hasHardwareState)
        {
            if (hardwareState.CpuTemperature.HasValue || hardwareState.BatteryTemperature.HasValue)
            {
                text.AppendLine().Append("CPU: ");
                AppendTemperature(text, hardwareState.CpuTemperature);
                text.Append(Label(compact, "; бат.: ", "; батарея: ", "; battery: ", "；电池："));
                AppendTemperature(text, hardwareState.BatteryTemperature);
                if (hardwareState.MemoryTemperature.HasValue)
                {
                    // Zone 0x0B (DDRS in the firmware): the sensor this machine's
                    // right fan actually ramps for, so it explains noise that CPU
                    // temperature alone does not.
                    text.Append(Label(compact, "; пам.: ", "; память: ", "; memory: ", "；内存："));
                    AppendTemperature(text, hardwareState.MemoryTemperature);
                }
            }
            if (hardwareState.Fan1Rpm.HasValue || hardwareState.Fan2Rpm.HasValue)
            {
                text.AppendLine().Append(Label(compact, "Вент.: ", "Вентиляторы: ", "Fans: ", "风扇："));
                AppendFan(text, hardwareState.Fan1Rpm);
                text.Append('/');
                AppendFan(text, hardwareState.Fan2Rpm);
                text.Append(L.T(" об/мин", " RPM", " 转/分"));
            }
        }

        return text.ToString();
    }

    private static void AppendTemperature(System.Text.StringBuilder text, int? value)
    {
        if (value.HasValue)
            text.Append(value.Value).Append("°C");
        else
            text.Append('?');
    }

    private static void AppendFan(System.Text.StringBuilder text, int? value)
    {
        if (value.HasValue)
            text.Append(value.Value);
        else
            text.Append('?');
    }

    private static void AppendPower(System.Text.StringBuilder text, double watts)
    {
        if (Math.Abs(watts) < 0.05)
        {
            // A charge limiter that already holds the battery inside its range, or
            // a fully charged one, legitimately reports neither charge nor
            // discharge. Spelling that out keeps "0 W" from reading as a dead
            // sensor on machines whose EC reports no rate at all.
            text.Append(L.T("0 Вт, без тока",
                "0 W, idle",
                "0 瓦（未充放电）"));
            return;
        }

        if (watts > 0)
            text.Append('+');
        text.Append(watts.ToString("0.0")).Append(L.T(" Вт", " W", " 瓦"));
    }

    // Charge/discharge power in watts: positive while charging, negative while
    // discharging. Read from the standard root\wmi BatteryStatus class, which is
    // available without administrator rights, and cached briefly so repeated
    // tooltip refreshes during a hover don't re-query WMI each time.
    private static double? ReadBatteryPowerWatts()
    {
        // Кэшируется и отсутствие значения: иначе на машине без батареи WMI
        // опрашивался бы при каждой перерисовке подсказки.
        var cached = Volatile.Read(ref _power);
        if (cached is not null && Environment.TickCount64 - cached.Ticks < PowerCacheMilliseconds)
            return cached.Watts;

        double? result = null;
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(
                scope, new ObjectQuery("SELECT ChargeRate, DischargeRate FROM BatteryStatus"));
            using var items = searcher.Get();
            foreach (ManagementBaseObject item in items)
            {
                using (item)
                {
                    var charge = Convert.ToInt64(item["ChargeRate"] ?? 0L);
                    var discharge = Convert.ToInt64(item["DischargeRate"] ?? 0L);
                    result = (charge - discharge) / 1000.0;
                }
                break;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read battery power", exception);
        }

        Volatile.Write(ref _power, new PowerSample(result, Environment.TickCount64));
        return result;
    }
}
